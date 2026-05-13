namespace Test.Xunit.Llm
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="GenericOpenAiAdapter"/> class.
    /// Tests request building, response normalization, and SSE streaming parsing.
    /// </summary>
    public class GenericOpenAiAdapterTests
    {
        #region Private-Members

        private readonly GenericOpenAiAdapter _Adapter = new GenericOpenAiAdapter();
        private readonly EndpointConfig _Endpoint = new EndpointConfig
        {
            Name = "test",
            BaseUrl = "http://localhost:8080",
            Model = "test-model",
            AdapterType = AdapterTypeEnum.OpenAiCompatible,
            Quirks = new BackendQuirks()
        };

        #endregion

        #region BuildRequest

        /// <summary>
        /// Verifies that BuildRequest includes the model and messages in the request body.
        /// </summary>
        [Fact]
        public async Task BuildRequest_IncludesModelAndMessages()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                Quirks = new BackendQuirks()
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>();

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            Assert.NotNull(parsed);
            Assert.Equal("test-model", parsed!["model"]!.GetValue<string>());

            JsonArray? messagesArray = parsed["messages"]?.AsArray();
            Assert.NotNull(messagesArray);
            Assert.Single(messagesArray!);
            Assert.Equal("user", messagesArray[0]!["role"]!.GetValue<string>());
            Assert.Equal("Hello", messagesArray[0]!["content"]!.GetValue<string>());
        }

        /// <summary>
        /// Verifies that BuildRequest includes tool definitions when tools are present.
        /// </summary>
        [Fact]
        public async Task BuildRequest_IncludesToolsWhenPresent()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                Quirks = new BackendQuirks()
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = "read_file",
                    Description = "Reads a file",
                    ParametersSchema = new { type = "object", properties = new { path = new { type = "string" } } }
                }
            };

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            JsonArray? toolsArray = parsed!["tools"]?.AsArray();
            Assert.NotNull(toolsArray);
            Assert.Single(toolsArray!);
            Assert.Equal("function", toolsArray[0]!["type"]!.GetValue<string>());
            Assert.Equal("read_file", toolsArray[0]!["function"]!["name"]!.GetValue<string>());
        }

        /// <summary>
        /// Verifies that BuildRequest omits the tools field when no tools are provided.
        /// </summary>
        [Fact]
        public async Task BuildRequest_NoTools_OmitsToolsField()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                Quirks = new BackendQuirks()
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>();

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            Assert.Null(parsed!["tools"]);
        }

        /// <summary>
        /// Verifies that non-streaming requests set the stream flag to false.
        /// </summary>
        [Fact]
        public async Task BuildRequest_NonStreaming_SetsStreamFalse()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                Quirks = new BackendQuirks()
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            HttpRequestMessage request = _Adapter.BuildRequest(messages, new List<ToolDefinition>(), endpoint, stream: false);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            Assert.NotNull(parsed);
            Assert.False(parsed!["stream"]!.GetValue<bool>());
        }

        /// <summary>
        /// Verifies that BuildRequest sets custom headers from the endpoint configuration.
        /// </summary>
        [Fact]
        public void BuildRequest_WithHeaders_SetsCustomHeaders()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                Headers = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer sk-testkey123" },
                    { "x-api-key", "custom-key" }
                },
                Quirks = new BackendQuirks()
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>();

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("sk-testkey123", request.Headers.Authorization.Parameter);
            Assert.True(request.Headers.Contains("x-api-key"));
        }

        /// <summary>
        /// Verifies that fields listed in StripRequestFields are removed from the request body.
        /// </summary>
        [Fact]
        public async Task BuildRequest_StripRequestFields_RemovesFields()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                Quirks = new BackendQuirks
                {
                    StripRequestFields = new List<string> { "temperature", "max_tokens" }
                }
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>();

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            Assert.Null(parsed!["temperature"]);
            Assert.Null(parsed!["max_tokens"]);
            Assert.NotNull(parsed!["model"]);
        }

        #endregion

        #region NormalizeFinalResponse

        /// <summary>
        /// Verifies that a text-only response is normalized with the correct content.
        /// </summary>
        [Fact]
        public void NormalizeFinalResponse_TextOnly_ExtractsContent()
        {
            string json = @"{
                ""choices"": [{
                    ""message"": {
                        ""role"": ""assistant"",
                        ""content"": ""Hello, world!""
                    }
                }]
            }";

            JsonElement element = JsonDocument.Parse(json).RootElement;

            ConversationMessage result = _Adapter.NormalizeFinalResponse(element);

            Assert.Equal(RoleEnum.Assistant, result.Role);
            Assert.Equal("Hello, world!", result.Content);
            Assert.Null(result.ToolCalls);
        }

        /// <summary>
        /// Verifies that a response with tool calls is normalized with all tool call fields extracted.
        /// </summary>
        [Fact]
        public void NormalizeFinalResponse_WithToolCalls_ExtractsAll()
        {
            string json =
                "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null," +
                "\"tool_calls\":[{\"id\":\"call_abc123\",\"type\":\"function\"," +
                "\"function\":{\"name\":\"read_file\"," +
                "\"arguments\":\"{\\\"path\\\":\\\"test.txt\\\"}\"}}]}}]}";

            JsonElement element = JsonDocument.Parse(json).RootElement;

            ConversationMessage result = _Adapter.NormalizeFinalResponse(element);

            Assert.Equal(RoleEnum.Assistant, result.Role);
            Assert.NotNull(result.ToolCalls);
            Assert.Single(result.ToolCalls!);
            Assert.Equal("call_abc123", result.ToolCalls![0].Id);
            Assert.Equal("read_file", result.ToolCalls[0].Name);
            Assert.Contains("test.txt", result.ToolCalls[0].Arguments);
        }

        #endregion

        #region ReadStreamingEvents

        /// <summary>
        /// Verifies that text content chunks are yielded as AssistantTextEvent instances.
        /// </summary>
        [Fact]
        public async Task ReadStreamingEvents_TextChunks_YieldsAssistantTextEvents()
        {
            string sseData =
                "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\" world\"},\"finish_reason\":null}]}\n\n" +
                "data: [DONE]\n\n";

            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));

            List<AgentEvent> events = new List<AgentEvent>();
            await foreach (AgentEvent agentEvent in _Adapter.ReadStreamingEvents(stream, _Endpoint, CancellationToken.None))
            {
                events.Add(agentEvent);
            }

            Assert.Equal(2, events.Count);
            Assert.IsType<AssistantTextEvent>(events[0]);
            Assert.IsType<AssistantTextEvent>(events[1]);
            Assert.Equal("Hello", ((AssistantTextEvent)events[0]).Text);
            Assert.Equal(" world", ((AssistantTextEvent)events[1]).Text);
        }

        /// <summary>
        /// Verifies that tool call deltas are assembled and yielded as ToolCallProposedEvent instances.
        /// </summary>
        [Fact]
        public async Task ReadStreamingEvents_ToolCallDeltas_AssemblesAndYields()
        {
            string sseData =
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call_1\",\"function\":{\"name\":\"read_file\",\"arguments\":\"\"}}]},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"path\\\"\"}}]},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\":\\\"file.txt\\\"}\"}}]},\"finish_reason\":null}]}\n\n" +
                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"tool_calls\"}]}\n\n" +
                "data: [DONE]\n\n";

            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));

            List<AgentEvent> events = new List<AgentEvent>();
            await foreach (AgentEvent agentEvent in _Adapter.ReadStreamingEvents(stream, _Endpoint, CancellationToken.None))
            {
                events.Add(agentEvent);
            }

            // Should have at least one ToolCallProposedEvent
            List<ToolCallProposedEvent> toolEvents = new List<ToolCallProposedEvent>();
            foreach (AgentEvent e in events)
            {
                if (e is ToolCallProposedEvent tce)
                {
                    toolEvents.Add(tce);
                }
            }

            Assert.NotEmpty(toolEvents);
            Assert.Equal("call_1", toolEvents[0].ToolCall.Id);
            Assert.Equal("read_file", toolEvents[0].ToolCall.Name);
            Assert.Contains("file.txt", toolEvents[0].ToolCall.Arguments);
        }

        /// <summary>
        /// Verifies that the [DONE] marker terminates the stream.
        /// </summary>
        [Fact]
        public async Task ReadStreamingEvents_DoneMarker_Terminates()
        {
            string sseData =
                "data: {\"choices\":[{\"delta\":{\"content\":\"Hi\"},\"finish_reason\":null}]}\n\n" +
                "data: [DONE]\n\n" +
                "data: {\"choices\":[{\"delta\":{\"content\":\"SHOULD NOT APPEAR\"},\"finish_reason\":null}]}\n\n";

            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));

            List<AgentEvent> events = new List<AgentEvent>();
            await foreach (AgentEvent agentEvent in _Adapter.ReadStreamingEvents(stream, _Endpoint, CancellationToken.None))
            {
                events.Add(agentEvent);
            }

            Assert.Single(events);
            Assert.IsType<AssistantTextEvent>(events[0]);
            Assert.Equal("Hi", ((AssistantTextEvent)events[0]).Text);
        }

        /// <summary>
        /// Verifies that malformed tool-call recovery can be disabled for freeform assistant text.
        /// </summary>
        [Fact]
        public async Task ReadStreamingEvents_MalformedRecoveryDisabled_DoesNotInferToolCalls()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "http://localhost:8080",
                Model = "test-model",
                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                Quirks = new BackendQuirks
                {
                    EnableMalformedToolCallRecovery = false
                }
            };

            string sseData =
                "data: {\"choices\":[{\"delta\":{\"content\":\"```json\\n{\\\"name\\\":\\\"read_file\\\",\\\"arguments\\\":{\\\"path\\\":\\\"example.txt\\\"}}\\n```\"},\"finish_reason\":\"stop\"}]}\n\n" +
                "data: [DONE]\n\n";

            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(sseData));

            List<AgentEvent> events = new List<AgentEvent>();
            await foreach (AgentEvent agentEvent in _Adapter.ReadStreamingEvents(stream, endpoint, CancellationToken.None))
            {
                events.Add(agentEvent);
            }

            Assert.Single(events);
            AssistantTextEvent textEvent = Assert.IsType<AssistantTextEvent>(events[0]);
            Assert.Contains("read_file", textEvent.Text, StringComparison.Ordinal);
        }

        #endregion
    }
}
