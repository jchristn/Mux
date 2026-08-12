namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite that exercises the PolyPrompt-backed <see cref="LlmClient"/> bridge against a
    /// local server emulating each supported backend protocol (OpenAI-compatible and Ollama-native). It
    /// verifies streamed assistant text, assembled tool calls, provider token usage, HTTP and connection
    /// error handling, the retry-without-tools fallback, the non-streaming path, and per-endpoint headers.
    /// </summary>
    public static class LlmBridgeSuite
    {
        private const string SuiteId = "LlmBridge";

        /// <summary>
        /// Builds the bridge suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the bridge cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "PolyPrompt LlmClient bridge against per-provider mock servers",
                new List<TestCaseDescriptor>
                {
                    Case("OpenAiBridge", "OpenAI-compatible adapter maps streaming, tools, usage, and errors", ct => RunAdapterScenariosAsync(AdapterTypeEnum.OpenAi, ct)),
                    Case("OpenAiCompatibleBridge", "openai-compatible adapter maps streaming, tools, usage, and errors", ct => RunAdapterScenariosAsync(AdapterTypeEnum.OpenAiCompatible, ct)),
                    Case("VllmBridge", "vLLM adapter maps streaming, tools, usage, and errors", ct => RunAdapterScenariosAsync(AdapterTypeEnum.Vllm, ct)),
                    Case("OllamaBridge", "Ollama adapter maps streaming, tools, usage, and errors", ct => RunAdapterScenariosAsync(AdapterTypeEnum.Ollama, ct)),
                    Case("OllamaBridgeToleratesV1BaseUrl", "Ollama adapter strips a trailing /v1 and reaches the native /api/chat", RunOllamaV1BaseUrlAsync),
                    Case("ConnectionFailureRetriesAndClassifies", "A connection failure retries and surfaces llm_connection_error", RunConnectionFailureAsync),
                    Case("ReasoningEffortReachesTheWire", "Reasoning effort maps onto the outbound request per adapter", RunReasoningEffortWireAsync),
                });
        }

        private static async Task RunAdapterScenariosAsync(AdapterTypeEnum adapterType, CancellationToken ct)
        {
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(adapterType, server.Endpoint);
            endpoint.Headers["X-Mux-Test"] = "marker";

            using LlmClient client = new LlmClient(endpoint);

            // 1. Text streaming: deltas assemble into text; provider usage is captured; no tool calls.
            List<AgentEvent> text = await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.AreEqual("hello world", AssistantText(text), $"{adapterType}: streamed text assembles");
            MuxAssert.IsFalse(HasToolCall(text), $"{adapterType}: no tool calls in a plain text turn");
            MuxAssert.IsNotNull(client.LastUsage, $"{adapterType}: usage captured");
            MuxAssert.AreEqual(3, client.LastUsage!.InputTokens, $"{adapterType}: input tokens");
            MuxAssert.AreEqual(2, client.LastUsage!.OutputTokens, $"{adapterType}: output tokens");

            // 2. Tool streaming: text plus an assembled tool call with id/name/arguments.
            List<AgentEvent> tool = await CollectAsync(client.StreamAsync(Messages("toolcall"), WeatherTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.Contains("Checking weather", AssistantText(tool), $"{adapterType}: tool turn streams preamble text");
            ToolCall? call = FirstToolCall(tool);
            MuxAssert.IsNotNull(call, $"{adapterType}: a tool call is proposed");
            MuxAssert.AreEqual("get_weather", call!.Name, $"{adapterType}: tool call name");
            MuxAssert.Contains("Seattle", call!.Arguments, $"{adapterType}: tool call arguments assembled");
            MuxAssert.AreEqual(7, client.LastUsage!.OutputTokens, $"{adapterType}: tool turn output tokens");

            // 3. HTTP error status surfaces as a single llm_error event.
            List<AgentEvent> error = await CollectAsync(client.StreamAsync(Messages("please http500"), NoTools(), ct), ct).ConfigureAwait(false);
            ErrorEvent? errorEvent = FirstError(error);
            MuxAssert.IsNotNull(errorEvent, $"{adapterType}: an error event is surfaced");
            MuxAssert.AreEqual("llm_error", errorEvent!.Code, $"{adapterType}: HTTP error classified as llm_error");
            MuxAssert.IsFalse(HasAssistantText(error), $"{adapterType}: no assistant text on an HTTP error");

            // 4. Retry-without-tools: a "does not support tools" rejection retries without tools and succeeds.
            int before = server.RequestCount;
            List<AgentEvent> unsupported = await CollectAsync(client.StreamAsync(Messages("unsupported request"), WeatherTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.AreEqual("hello world", AssistantText(unsupported), $"{adapterType}: retry without tools succeeds");
            MuxAssert.AreEqual(before + 2, server.RequestCount, $"{adapterType}: one rejected request plus one retry");

            // 5. Non-streaming SendAsync returns a normalized assistant message.
            ConversationMessage reply = await client.SendAsync(Messages("hi"), NoTools(), ct).ConfigureAwait(false);
            MuxAssert.AreEqual("pong", reply.Content, $"{adapterType}: non-streaming reply content");

            // 6. Per-endpoint headers reach the backend.
            MuxAssert.AreEqual("marker", server.HeaderValue("X-Mux-Test"), $"{adapterType}: endpoint headers are applied");
        }

        private static async Task RunOllamaV1BaseUrlAsync(CancellationToken ct)
        {
            // mux's own defaults, docs, and add-endpoint form historically appended /v1 to ollama base
            // URLs. The native Ollama API lives at the server root (/api/chat), so a /v1 suffix yields a
            // "404 page not found". The Ollama adapter must strip it and still succeed.
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.Ollama, server.Endpoint + "/v1");

            using LlmClient client = new LlmClient(endpoint);

            List<AgentEvent> text = await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.IsNull(FirstError(text), "Ollama with a /v1 base URL does not 404");
            MuxAssert.AreEqual("hello world", AssistantText(text), "Ollama with a /v1 base URL still reaches /api/chat");
        }

        private static async Task RunConnectionFailureAsync(CancellationToken ct)
        {
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAi, "http://127.0.0.1:1");
            endpoint.TimeoutMs = 2000;

            int retries = 0;
            using LlmClient client = new LlmClient(endpoint)
            {
                OnRetry = (int attempt, int maxRetries, string message) => Interlocked.Increment(ref retries)
            };

            List<AgentEvent> events = await CollectAsync(client.StreamAsync(Messages("hi"), NoTools(), ct), ct).ConfigureAwait(false);
            ErrorEvent? error = FirstError(events);
            MuxAssert.IsNotNull(error, "a connection error is surfaced");
            MuxAssert.AreEqual("llm_connection_error", error!.Code, "connection failure classified as llm_connection_error");
            MuxAssert.IsTrue(retries >= 1, "the connection failure is retried");
        }

        private static async Task RunReasoningEffortWireAsync(CancellationToken ct)
        {
            // OpenAI-compatible: a High level sends reasoning_effort "high".
            using (LocalLlmTestServer server = LocalLlmTestServer.Start())
            {
                EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAi, server.Endpoint);
                endpoint.ReasoningEffort = new ReasoningEffortConfig { Level = ReasoningLevelEnum.High };
                using LlmClient client = new LlmClient(endpoint);
                await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
                MuxAssert.Contains("\"reasoning_effort\":\"high\"", server.RequestBodies[0], "OpenAI adapter sends reasoning_effort high");
            }

            // Unset: no reasoning field is sent, so existing requests are byte-for-byte unchanged.
            using (LocalLlmTestServer server = LocalLlmTestServer.Start())
            {
                EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAi, server.Endpoint);
                using LlmClient client = new LlmClient(endpoint);
                await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
                MuxAssert.IsFalse(server.RequestBodies[0].Contains("reasoning_effort", StringComparison.Ordinal), "no reasoning_effort is sent by default");
            }

            // A Minimal level overrides to reasoning_effort "minimal".
            using (LocalLlmTestServer server = LocalLlmTestServer.Start())
            {
                EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAi, server.Endpoint);
                endpoint.ReasoningEffort = new ReasoningEffortConfig { Level = ReasoningLevelEnum.Minimal };
                using LlmClient client = new LlmClient(endpoint);
                await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
                MuxAssert.Contains("\"reasoning_effort\":\"minimal\"", server.RequestBodies[0], "OpenAI adapter sends reasoning_effort minimal");
            }

            // Ollama-native: a Medium level sends think "medium".
            using (LocalLlmTestServer server = LocalLlmTestServer.Start())
            {
                EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.Ollama, server.Endpoint);
                endpoint.ReasoningEffort = new ReasoningEffortConfig { Level = ReasoningLevelEnum.Medium };
                using LlmClient client = new LlmClient(endpoint);
                await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
                MuxAssert.Contains("\"think\":\"medium\"", server.RequestBodies[0], "Ollama adapter sends think medium");
            }
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static EndpointConfig MakeEndpoint(AdapterTypeEnum adapterType, string baseUrl)
        {
            return new EndpointConfig
            {
                Name = "local",
                AdapterType = adapterType,
                BaseUrl = baseUrl,
                Model = "test-model",
                TimeoutMs = 5000
            };
        }

        private static List<ConversationMessage> Messages(string content)
        {
            return new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = content }
            };
        }

        private static List<ToolDefinition> NoTools()
        {
            return new List<ToolDefinition>();
        }

        private static List<ToolDefinition> WeatherTools()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = "get_weather",
                    Description = "Get the current weather for a city.",
                    ParametersSchema = new Dictionary<string, object>
                    {
                        { "type", "object" },
                        {
                            "properties", new Dictionary<string, object>
                            {
                                { "city", new Dictionary<string, object> { { "type", "string" } } }
                            }
                        },
                        { "required", new List<string> { "city" } }
                    }
                }
            };
        }

        private static async Task<List<AgentEvent>> CollectAsync(IAsyncEnumerable<AgentEvent> events, CancellationToken ct)
        {
            List<AgentEvent> list = new List<AgentEvent>();
            await foreach (AgentEvent agentEvent in events.WithCancellation(ct).ConfigureAwait(false))
            {
                list.Add(agentEvent);
            }

            return list;
        }

        private static string AssistantText(List<AgentEvent> events)
        {
            StringBuilder builder = new StringBuilder();
            foreach (AgentEvent agentEvent in events)
            {
                if (agentEvent is AssistantTextEvent textEvent)
                {
                    builder.Append(textEvent.Text);
                }
            }

            return builder.ToString();
        }

        private static bool HasAssistantText(List<AgentEvent> events)
        {
            foreach (AgentEvent agentEvent in events)
            {
                if (agentEvent is AssistantTextEvent textEvent && !string.IsNullOrEmpty(textEvent.Text))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasToolCall(List<AgentEvent> events)
        {
            return FirstToolCall(events) != null;
        }

        private static ToolCall? FirstToolCall(List<AgentEvent> events)
        {
            foreach (AgentEvent agentEvent in events)
            {
                if (agentEvent is ToolCallProposedEvent proposed)
                {
                    return proposed.ToolCall;
                }
            }

            return null;
        }

        private static ErrorEvent? FirstError(List<AgentEvent> events)
        {
            foreach (AgentEvent agentEvent in events)
            {
                if (agentEvent is ErrorEvent errorEvent)
                {
                    return errorEvent;
                }
            }

            return null;
        }

        #endregion
    }
}
