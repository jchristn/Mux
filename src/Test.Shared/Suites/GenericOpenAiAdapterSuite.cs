namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
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
    /// Touchstone suite for <see cref="GenericOpenAiAdapter"/> request building, response normalization,
    /// and SSE streaming parsing. Ported from the <c>GenericOpenAiAdapterTests</c> xUnit suite.
    /// </summary>
    public static class GenericOpenAiAdapterSuite
    {
        private static GenericOpenAiAdapter Adapter() => new GenericOpenAiAdapter();

        private static EndpointConfig BasicEndpoint(BackendQuirks? quirks = null)
        {
            return new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                Quirks = quirks ?? new BackendQuirks()
            };
        }

        private static List<ConversationMessage> HelloMessages()
        {
            return new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "Hello" } };
        }

        private static async Task<List<AgentEvent>> ReadStreamAsync(GenericOpenAiAdapter adapter, string sseData, EndpointConfig endpoint, CancellationToken ct)
        {
            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));
            List<AgentEvent> events = new List<AgentEvent>();
            await foreach (AgentEvent agentEvent in adapter.ReadStreamingEvents(stream, endpoint, ct).ConfigureAwait(false))
            {
                events.Add(agentEvent);
            }
            return events;
        }

        /// <summary>
        /// Builds the generic-OpenAI-adapter suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the generic-OpenAI-adapter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "GenericOpenAiAdapter",
                "Generic OpenAI-compatible adapter behavior",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("GenericOpenAiAdapter", "BuildRequestIncludesModelAndMessages", "BuildRequest includes the model and messages", async (CancellationToken ct) =>
                    {
                        HttpRequestMessage request = Adapter().BuildRequest(HelloMessages(), new List<ToolDefinition>(), BasicEndpoint());
                        OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                        MuxAssert.IsNotNull(parsed, "parsed");
                        MuxAssert.AreEqual("test-model", parsed!.Model, "model");
                        MuxAssert.AreEqual(1, parsed.Messages.Count, "message count");
                        MuxAssert.AreEqual("user", parsed.Messages[0].Role, "role");
                        MuxAssert.AreEqual("Hello", parsed.Messages[0].Content, "content");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "BuildRequestIncludesToolsWhenPresent", "BuildRequest includes tool definitions when present", async (CancellationToken ct) =>
                    {
                        List<ToolDefinition> tools = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "read_file", Description = "Reads a file", ParametersSchema = new { type = "object", properties = new { path = new { type = "string" } } } }
                        };
                        HttpRequestMessage request = Adapter().BuildRequest(HelloMessages(), tools, BasicEndpoint());
                        OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                        MuxAssert.IsNotNull(parsed?.Tools, "tools not null");
                        MuxAssert.AreEqual(1, parsed!.Tools!.Count, "tool count");
                        MuxAssert.AreEqual("function", parsed.Tools![0].Type, "tool type");
                        MuxAssert.AreEqual("read_file", parsed.Tools[0].Function.Name, "tool name");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "BuildRequestNoToolsOmitsToolsField", "BuildRequest omits the tools field when no tools are provided", async (CancellationToken ct) =>
                    {
                        HttpRequestMessage request = Adapter().BuildRequest(HelloMessages(), new List<ToolDefinition>(), BasicEndpoint());
                        OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                        MuxAssert.IsNull(parsed!.Tools, "tools omitted");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "BuildRequestNonStreamingSetsStreamFalse", "Non-streaming requests set the stream flag to false", async (CancellationToken ct) =>
                    {
                        HttpRequestMessage request = Adapter().BuildRequest(HelloMessages(), new List<ToolDefinition>(), BasicEndpoint(), stream: false);
                        OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                        MuxAssert.IsNotNull(parsed, "parsed");
                        MuxAssert.IsTrue(parsed!.Stream == false, "stream false");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "BuildRequestWithHeadersSetsCustomHeaders", "BuildRequest sets custom headers from the endpoint configuration", (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = BasicEndpoint();
                        endpoint.Headers = new Dictionary<string, string> { { "Authorization", "Bearer sk-testkey123" }, { "x-api-key", "custom-key" } };
                        HttpRequestMessage request = Adapter().BuildRequest(HelloMessages(), new List<ToolDefinition>(), endpoint);
                        MuxAssert.IsNotNull(request.Headers.Authorization, "authorization header");
                        MuxAssert.AreEqual("Bearer", request.Headers.Authorization!.Scheme, "scheme");
                        MuxAssert.AreEqual("sk-testkey123", request.Headers.Authorization.Parameter, "parameter");
                        MuxAssert.IsTrue(request.Headers.Contains("x-api-key"), "x-api-key present");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "BuildRequestStripRequestFieldsRemovesFields", "Fields listed in StripRequestFields are removed from the request body", async (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = BasicEndpoint(new BackendQuirks { StripRequestFields = new List<string> { "temperature", "max_tokens" } });
                        HttpRequestMessage request = Adapter().BuildRequest(HelloMessages(), new List<ToolDefinition>(), endpoint);
                        OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(await request.Content!.ReadAsStringAsync().ConfigureAwait(false));
                        MuxAssert.IsNull(parsed!.Temperature, "temperature stripped");
                        MuxAssert.IsNull(parsed.MaxTokens, "max_tokens stripped");
                        MuxAssert.AreEqual("test-model", parsed.Model, "model retained");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "NormalizeFinalResponseTextOnlyExtractsContent", "A text-only response is normalized with the correct content", (CancellationToken ct) =>
                    {
                        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"Hello, world!\"}}]}";
                        OpenAiChatCompletionResponse? element = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(json);
                        ConversationMessage result = Adapter().NormalizeFinalResponse(element!);
                        MuxAssert.AreEqual(RoleEnum.Assistant, result.Role, "role");
                        MuxAssert.AreEqual("Hello, world!", result.Content, "content");
                        MuxAssert.IsNull(result.ToolCalls, "no tool calls");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "NormalizeFinalResponseWithToolCallsExtractsAll", "A response with tool calls is normalized with all fields extracted", (CancellationToken ct) =>
                    {
                        string json = "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call_abc123\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"test.txt\\\"}\"}}]}}]}";
                        OpenAiChatCompletionResponse? element = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(json);
                        ConversationMessage result = Adapter().NormalizeFinalResponse(element!);
                        MuxAssert.AreEqual(RoleEnum.Assistant, result.Role, "role");
                        MuxAssert.IsNotNull(result.ToolCalls, "tool calls not null");
                        MuxAssert.AreEqual(1, result.ToolCalls!.Count, "tool call count");
                        MuxAssert.AreEqual("call_abc123", result.ToolCalls![0].Id, "tool call id");
                        MuxAssert.AreEqual("read_file", result.ToolCalls[0].Name, "tool call name");
                        MuxAssert.Contains("test.txt", result.ToolCalls[0].Arguments, "arguments");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "ReadStreamingEventsTextChunksYieldsAssistantTextEvents", "Text content chunks are yielded as AssistantTextEvent instances", async (CancellationToken ct) =>
                    {
                        string sseData =
                            "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n" +
                            "data: {\"choices\":[{\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}\n\n" +
                            "data: [DONE]\n\n";
                        List<AgentEvent> events = await ReadStreamAsync(Adapter(), sseData, BasicEndpoint(), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(2, events.Count, "event count");
                        MuxAssert.IsType<AssistantTextEvent>(events[0], "event 0 type");
                        MuxAssert.IsType<AssistantTextEvent>(events[1], "event 1 type");
                        MuxAssert.AreEqual("Hello", ((AssistantTextEvent)events[0]).Text, "text 0");
                        MuxAssert.AreEqual(" world", ((AssistantTextEvent)events[1]).Text, "text 1");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "ReadStreamingEventsToolCallDeltasAssemblesAndYields", "Tool call deltas are assembled and yielded as ToolCallProposedEvent instances", async (CancellationToken ct) =>
                    {
                        string sseData =
                            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}\n\n" +
                            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"path\\\"\"}}]},\"finish_reason\":null}]}\n\n" +
                            "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\":\\\"file.txt\\\"}\"}}]},\"finish_reason\":null}]}\n\n" +
                            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n" +
                            "data: [DONE]\n\n";
                        List<AgentEvent> events = await ReadStreamAsync(Adapter(), sseData, BasicEndpoint(), ct).ConfigureAwait(false);

                        List<ToolCallProposedEvent> toolEvents = new List<ToolCallProposedEvent>();
                        foreach (AgentEvent e in events)
                        {
                            if (e is ToolCallProposedEvent tce) toolEvents.Add(tce);
                        }

                        MuxAssert.IsTrue(toolEvents.Count > 0, "at least one tool event");
                        MuxAssert.AreEqual("call_1", toolEvents[0].ToolCall.Id, "tool call id");
                        MuxAssert.AreEqual("read_file", toolEvents[0].ToolCall.Name, "tool call name");
                        MuxAssert.Contains("file.txt", toolEvents[0].ToolCall.Arguments, "arguments");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "ReadStreamingEventsDoneMarkerTerminates", "The [DONE] marker terminates the stream", async (CancellationToken ct) =>
                    {
                        string sseData =
                            "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}\n\n" +
                            "data: [DONE]\n\n" +
                            "data: {\"choices\":[{\"delta\":{\"content\":\"SHOULD NOT APPEAR\"},\"finish_reason\":null}]}\n\n";
                        List<AgentEvent> events = await ReadStreamAsync(Adapter(), sseData, BasicEndpoint(), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, events.Count, "single event");
                        MuxAssert.IsType<AssistantTextEvent>(events[0], "event type");
                        MuxAssert.AreEqual("Hi", ((AssistantTextEvent)events[0]).Text, "text");
                    }),

                    new TestCaseDescriptor("GenericOpenAiAdapter", "ReadStreamingEventsMalformedRecoveryDisabled", "Malformed tool-call recovery can be disabled for freeform assistant text", async (CancellationToken ct) =>
                    {
                        EndpointConfig endpoint = BasicEndpoint(new BackendQuirks { EnableMalformedToolCallRecovery = false });
                        string sseData =
                            "data: {\"choices\":[{\"delta\":{\"content\":\"```json\\n{\\\"name\\\":\\\"read_file\\\",\\\"arguments\\\":{\\\"path\\\":\\\"example.txt\\\"}}\\n```\"},\"finish_reason\":\"stop\"}]}\n\n" +
                            "data: [DONE]\n\n";
                        List<AgentEvent> events = await ReadStreamAsync(Adapter(), sseData, endpoint, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, events.Count, "single event");
                        AssistantTextEvent textEvent = MuxAssert.IsType<AssistantTextEvent>(events[0], "event type");
                        MuxAssert.Contains("read_file", textEvent.Text, "text contains read_file");
                    })
                });
        }
    }
}
