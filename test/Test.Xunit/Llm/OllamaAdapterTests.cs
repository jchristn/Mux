namespace Test.Xunit.Llm
{
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="OllamaAdapter"/> class.
    /// Tests that Ollama-specific fields are stripped from requests.
    /// </summary>
    public class OllamaAdapterTests
    {
        #region Private-Members

        private readonly OllamaAdapter _Adapter = new OllamaAdapter();

        #endregion

        #region Tests

        /// <summary>
        /// Verifies that parallel_tool_calls is stripped from the request body.
        /// </summary>
        [Fact]
        public async Task BuildRequest_StripsParallelToolCalls()
        {
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
                    ParametersSchema = new { type = "object" }
                }
            };

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            Assert.Null(parsed!["parallel_tool_calls"]);
        }

        /// <summary>
        /// Verifies that stream_options is stripped from the request body.
        /// </summary>
        [Fact]
        public async Task BuildRequest_StripsStreamOptions()
        {
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

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>();

            HttpRequestMessage request = _Adapter.BuildRequest(messages, tools, endpoint);

            string body = await request.Content!.ReadAsStringAsync();
            JsonNode? parsed = JsonNode.Parse(body);

            Assert.Null(parsed!["stream_options"]);
        }

        #endregion
    }
}
