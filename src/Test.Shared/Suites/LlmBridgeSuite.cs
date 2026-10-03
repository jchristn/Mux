namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
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
                    Case("ThinkingCapturedWhenEnabled", "Thinking is surfaced as separate events when the endpoint enables it", RunThinkingCapturedAsync),
                    Case("ThoughtSignatureRoundTrips", "A Gemini thought signature is captured, persisted, and replayed on the next request", RunThoughtSignatureRoundTripAsync),
                    Case("ThinkingSuppressedWhenDisabled", "No thinking events are produced when the endpoint disables it", RunThinkingSuppressedAsync),
                    Case("EndpointSettingsReachTheWire", "The endpoint's model and max tokens reach the outbound request per adapter", RunEndpointSettingsWireAsync),
                    Case("GeminiApiKeySentAsHeader", "The Gemini API key is sent as x-goog-api-key, never in the URL", RunGeminiApiKeyHeaderAsync),
                    Case("GeminiToolResultCarriesFunctionName", "Gemini functionResponse.name is the called function's name, not the call id", RunGeminiToolResultNameAsync),
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

        private static async Task RunThoughtSignatureRoundTripAsync(CancellationToken ct)
        {
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAiCompatible, server.Endpoint);
            using LlmClient client = new LlmClient(endpoint);

            // 1. The signature on the streamed tool call is captured onto mux's ToolCall.
            List<AgentEvent> events = await CollectAsync(client.StreamAsync(Messages("signedtool"), WeatherTools(), ct), ct).ConfigureAwait(false);
            ToolCall? call = FirstToolCall(events);
            MuxAssert.IsNotNull(call, "a signed tool call is proposed");
            MuxAssert.AreEqual("sig-abc123", call!.ThoughtSignature, "thought signature captured");

            // 2. It survives the session-store JSON round trip.
            ConversationMessage assistant = new ConversationMessage { Role = RoleEnum.Assistant, Content = string.Empty, ToolCalls = new List<ToolCall> { call } };
            string json = JsonSerializer.Serialize(assistant);
            ConversationMessage restored = JsonSerializer.Deserialize<ConversationMessage>(json)!;
            MuxAssert.AreEqual("sig-abc123", restored.ToolCalls![0].ThoughtSignature, "thought signature persisted");

            // 3. Replaying the restored history sends the signature back in Gemini's extra_content.
            List<ConversationMessage> history = Messages("signedtool");
            history.Add(restored);
            history.Add(new ConversationMessage { Role = RoleEnum.Tool, Content = "{\"temp\":61}", ToolCallId = call.Id });
            await CollectAsync(client.StreamAsync(history, NoTools(), ct), ct).ConfigureAwait(false);
            string replay = server.RequestBodies[server.RequestBodies.Count - 1];
            MuxAssert.Contains("\"thought_signature\":\"sig-abc123\"", replay, "thought signature replayed");

            // 4. An unsigned call neither serializes a signature nor sends extra_content.
            ToolCall unsigned = new ToolCall { Id = "c1", Name = "get_weather", Arguments = "{}" };
            MuxAssert.IsFalse(JsonSerializer.Serialize(unsigned).Contains("thoughtSignature", StringComparison.Ordinal), "no signature key when unsigned");
            List<ConversationMessage> plain = Messages("hello");
            plain.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = string.Empty, ToolCalls = new List<ToolCall> { unsigned } });
            plain.Add(new ConversationMessage { Role = RoleEnum.Tool, Content = "{}", ToolCallId = "c1" });
            await CollectAsync(client.StreamAsync(plain, NoTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.IsFalse(server.RequestBodies[server.RequestBodies.Count - 1].Contains("extra_content", StringComparison.Ordinal), "no extra_content for unsigned calls");
        }

        private static async Task RunEndpointSettingsWireAsync(CancellationToken ct)
        {
            // PolyPrompt 3 moved per-request model and token settings from ToolChatRequest onto its Options;
            // the endpoint's values must still land in the body, and no system message may be injected.
            foreach (AdapterTypeEnum adapterType in new[] { AdapterTypeEnum.OpenAi, AdapterTypeEnum.Anthropic })
            {
                using LocalLlmTestServer server = LocalLlmTestServer.Start();
                EndpointConfig endpoint = MakeEndpoint(adapterType, server.Endpoint);
                endpoint.Model = "wire-model";
                endpoint.MaxTokens = 2345;
                endpoint.ApiKey = "test-key";
                using LlmClient client = new LlmClient(endpoint);

                await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
                MuxAssert.IsTrue(server.RequestCount > 0, $"{adapterType}: the request reached the mock");

                string body = server.RequestBodies[0];
                MuxAssert.Contains("\"model\":\"wire-model\"", body, $"{adapterType}: endpoint model is sent");
                MuxAssert.Contains("\"max_tokens\":2345", body, $"{adapterType}: endpoint max tokens are sent");
                MuxAssert.IsFalse(body.Contains("\"system\"", StringComparison.Ordinal), $"{adapterType}: no system prompt is injected");
            }
        }

        private static async Task RunGeminiApiKeyHeaderAsync(CancellationToken ct)
        {
            // The mock does not speak Gemini's wire format; only the captured request is under test.
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.Gemini, server.Endpoint);
            endpoint.ApiKey = "gemini-secret";
            using LlmClient client = new LlmClient(endpoint);

            await CollectAsync(client.StreamAsync(Messages("hello"), NoTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.IsTrue(server.RequestCount > 0, "the Gemini request reached the mock");
            MuxAssert.AreEqual("gemini-secret", server.HeaderValue("x-goog-api-key"), "the API key is sent as x-goog-api-key");
            foreach (string query in server.RequestQueries)
            {
                MuxAssert.IsFalse(query.Contains("gemini-secret", StringComparison.Ordinal), "the API key is not in the request URL");
            }
        }

        private static async Task RunGeminiToolResultNameAsync(CancellationToken ct)
        {
            // The mock does not speak Gemini's wire format, so the request itself fails; the captured body is
            // what PolyPrompt's Gemini client serialized from mux's history, which is what is under test.
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.Gemini, server.Endpoint);
            endpoint.ApiKey = "test-key";
            using LlmClient client = new LlmClient(endpoint);

            // Parallel calls whose results arrive in the reverse order, plus a denied call's error result.
            List<ConversationMessage> history = Messages("weather please");
            history.Add(new ConversationMessage
            {
                Role = RoleEnum.Assistant,
                Content = string.Empty,
                ToolCalls = new List<ToolCall>
                {
                    new ToolCall { Id = "call_1", Name = "get_weather", Arguments = "{\"city\":\"Seattle\"}" },
                    new ToolCall { Id = "call_2", Name = "list_directory", Arguments = "{\"path\":\".\"}" }
                }
            });
            history.Add(new ConversationMessage { Role = RoleEnum.Tool, ToolCallId = "call_2", Content = "[FILE] a.txt" });
            history.Add(new ConversationMessage { Role = RoleEnum.Tool, ToolCallId = "call_1", Content = "{\"temp\":61}" });
            history.Add(new ConversationMessage
            {
                Role = RoleEnum.Assistant,
                Content = string.Empty,
                ToolCalls = new List<ToolCall> { new ToolCall { Id = "call_3", Name = "write_file", Arguments = "{}" } }
            });
            history.Add(new ConversationMessage { Role = RoleEnum.Tool, ToolCallId = "call_3", Content = "{\"error\":\"tool_call_denied\"}" });

            // A resumed session: the same history after the session-store JSON round trip.
            List<ConversationMessage> resumed = JsonSerializer.Deserialize<List<ConversationMessage>>(JsonSerializer.Serialize(history))!;

            foreach ((string label, List<ConversationMessage> messages) in new[] { ("live", history), ("resumed", resumed) })
            {
                int before = server.RequestBodies.Count;
                await CollectAsync(client.StreamAsync(messages, WeatherTools(), ct), ct).ConfigureAwait(false);
                MuxAssert.IsTrue(server.RequestBodies.Count > before, $"{label}: the Gemini request reached the mock");

                Dictionary<string, string> names = FunctionResponseNames(server.RequestBodies[server.RequestBodies.Count - 1]);
                MuxAssert.AreEqual(3, names.Count, $"{label}: every tool result is sent as a functionResponse");
                MuxAssert.AreEqual("get_weather", names["call_1"], $"{label}: call_1 result named get_weather");
                MuxAssert.AreEqual("list_directory", names["call_2"], $"{label}: call_2 result named list_directory");
                MuxAssert.AreEqual("write_file", names["call_3"], $"{label}: denied call_3 result named write_file");
            }
        }

        private static Dictionary<string, string> FunctionResponseNames(string body)
        {
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            using JsonDocument document = JsonDocument.Parse(body);
            foreach (JsonElement content in document.RootElement.GetProperty("contents").EnumerateArray())
            {
                if (!content.TryGetProperty("parts", out JsonElement parts)) continue;
                foreach (JsonElement part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("functionResponse", out JsonElement response))
                    {
                        names[response.GetProperty("id").GetString()!] = response.GetProperty("name").GetString()!;
                    }
                }
            }

            return names;
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

        private static async Task RunThinkingCapturedAsync(CancellationToken ct)
        {
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAi, server.Endpoint);
            endpoint.ShowThinking = true;

            using LlmClient client = new LlmClient(endpoint);

            List<AgentEvent> events = await CollectAsync(client.StreamAsync(Messages("reasoncapture please"), NoTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.AreEqual("Let me think.", ThinkingText(events), "thinking is captured as separate events");
            MuxAssert.AreEqual("hello world", AssistantText(events), "the answer text is unchanged");
            MuxAssert.IsFalse(AssistantText(events).Contains("think", StringComparison.Ordinal), "thinking does not leak into the answer text");
        }

        private static async Task RunThinkingSuppressedAsync(CancellationToken ct)
        {
            using LocalLlmTestServer server = LocalLlmTestServer.Start();
            EndpointConfig endpoint = MakeEndpoint(AdapterTypeEnum.OpenAi, server.Endpoint);
            // ShowThinking defaults to false.

            using LlmClient client = new LlmClient(endpoint);

            List<AgentEvent> events = await CollectAsync(client.StreamAsync(Messages("reasoncapture please"), NoTools(), ct), ct).ConfigureAwait(false);
            MuxAssert.AreEqual(string.Empty, ThinkingText(events), "no thinking events are produced when the endpoint has it off");
            MuxAssert.AreEqual("hello world", AssistantText(events), "the answer is still produced when thinking is off");
        }

        #region Helpers

        private static string ThinkingText(List<AgentEvent> events)
        {
            StringBuilder builder = new StringBuilder();
            foreach (AgentEvent agentEvent in events)
            {
                if (agentEvent is AssistantThinkingEvent thinkingEvent)
                {
                    builder.Append(thinkingEvent.Text);
                }
            }

            return builder.ToString();
        }

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
