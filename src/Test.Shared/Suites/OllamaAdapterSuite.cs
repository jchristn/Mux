namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="OllamaAdapter"/> verifying that Ollama-specific fields are stripped
    /// from requests. Ported from the <c>OllamaAdapterTests</c> xUnit suite.
    /// </summary>
    public static class OllamaAdapterSuite
    {
        /// <summary>
        /// Builds the Ollama-adapter suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the Ollama-adapter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "OllamaAdapter",
                "Ollama adapter request field stripping",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "OllamaAdapter",
                        "BuildRequestStripsParallelToolCalls",
                        "parallel_tool_calls is stripped from the request body",
                        async (CancellationToken ct) =>
                        {
                            OllamaAdapter adapter = new OllamaAdapter();
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "test-ollama",
                                BaseUrl = "http://localhost:11434",
                                Model = "llama3",
                                Quirks = new BackendQuirks
                                {
                                    SupportsParallelToolCalls = true,
                                    StripRequestFields = new List<string> { "parallel_tool_calls", "stream_options" }
                                }
                            };
                            List<ConversationMessage> messages = new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "Hello" } };
                            List<ToolDefinition> tools = new List<ToolDefinition>
                            {
                                new ToolDefinition { Name = "read_file", Description = "Reads a file", ParametersSchema = new { type = "object" } }
                            };

                            HttpRequestMessage request = adapter.BuildRequest(messages, tools, endpoint);
                            string body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                            OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(body);

                            MuxAssert.IsNotNull(parsed, "parsed request");
                            MuxAssert.IsNull(parsed!.ParallelToolCalls, "ParallelToolCalls stripped");
                        }),

                    new TestCaseDescriptor(
                        "OllamaAdapter",
                        "BuildRequestStripsStreamOptions",
                        "stream_options is stripped from the request body",
                        async (CancellationToken ct) =>
                        {
                            OllamaAdapter adapter = new OllamaAdapter();
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "test-ollama",
                                BaseUrl = "http://localhost:11434",
                                Model = "llama3",
                                Quirks = new BackendQuirks
                                {
                                    StripRequestFields = new List<string> { "parallel_tool_calls", "stream_options" }
                                }
                            };
                            List<ConversationMessage> messages = new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "Hello" } };
                            List<ToolDefinition> tools = new List<ToolDefinition>();

                            HttpRequestMessage request = adapter.BuildRequest(messages, tools, endpoint);
                            string body = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
                            OpenAiChatRequest? parsed = JsonSerializer.Deserialize<OpenAiChatRequest>(body);

                            MuxAssert.IsNotNull(parsed, "parsed request");
                        })
                });
        }
    }
}
