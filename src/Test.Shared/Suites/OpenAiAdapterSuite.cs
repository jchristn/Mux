namespace Test.Shared.Suites
{
    using System;
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
    /// Touchstone suite for <see cref="OpenAiAdapter"/> covering API key requirements and parallel tool
    /// call behavior. Ported from the <c>OpenAiAdapterTests</c> xUnit suite.
    /// </summary>
    public static class OpenAiAdapterSuite
    {
        /// <summary>
        /// Builds the OpenAI-adapter suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the OpenAI-adapter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "OpenAiAdapter",
                "OpenAI adapter request building",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "OpenAiAdapter",
                        "BuildRequestRequiresAuthorizationHeader",
                        "BuildRequest throws when no Authorization header is set",
                        (CancellationToken ct) =>
                        {
                            OpenAiAdapter adapter = new OpenAiAdapter();
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "test",
                                BaseUrl = "https://api.openai.com/v1",
                                Model = "gpt-4o",
                                Quirks = new BackendQuirks { SupportsParallelToolCalls = true }
                            };
                            List<ConversationMessage> messages = new List<ConversationMessage> { new ConversationMessage { Role = RoleEnum.User, Content = "Hello" } };
                            List<ToolDefinition> tools = new List<ToolDefinition>();

                            MuxAssert.Throws<InvalidOperationException>(() => adapter.BuildRequest(messages, tools, endpoint), "missing Authorization");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "OpenAiAdapter",
                        "BuildRequestSetsParallelToolCalls",
                        "BuildRequest sets parallel_tool_calls to true when tools are provided",
                        async (CancellationToken ct) =>
                        {
                            OpenAiAdapter adapter = new OpenAiAdapter();
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "test",
                                BaseUrl = "https://api.openai.com/v1",
                                Model = "gpt-4o",
                                Headers = new Dictionary<string, string> { { "Authorization", "Bearer sk-test" } },
                                Quirks = new BackendQuirks { SupportsParallelToolCalls = true }
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
                            MuxAssert.IsTrue(parsed!.ParallelToolCalls == true, "ParallelToolCalls true");
                        })
                });
        }
    }
}
