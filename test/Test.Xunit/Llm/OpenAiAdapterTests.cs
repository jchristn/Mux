namespace Test.Xunit.Llm
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;

    /// <summary>
    /// Unit tests for the <see cref="OpenAiAdapter"/> class.
    /// Tests API key requirement and parallel tool call behavior.
    /// </summary>
    public class OpenAiAdapterTests
    {
        #region Private-Members

        private readonly OpenAiAdapter _Adapter = new OpenAiAdapter();

        #endregion

        #region Tests

        /// <summary>
        /// Verifies that BuildRequest throws an InvalidOperationException when no API key is set.
        /// </summary>
        [Fact]
        public void BuildRequest_RequiresApiKey_ThrowsWithoutIt()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o",
                ApiKey = null,
                Quirks = new BackendQuirks { SupportsParallelToolCalls = true }
            };

            List<ConversationMessage> messages = new List<ConversationMessage>
            {
                new ConversationMessage { Role = RoleEnum.User, Content = "Hello" }
            };

            List<ToolDefinition> tools = new List<ToolDefinition>();

            Assert.Throws<InvalidOperationException>(() => _Adapter.BuildRequest(messages, tools, endpoint));
        }

        /// <summary>
        /// Verifies that BuildRequest sets parallel_tool_calls to true when tools are provided.
        /// </summary>
        [Fact]
        public async Task BuildRequest_SetsParallelToolCalls()
        {
            EndpointConfig endpoint = new EndpointConfig
            {
                Name = "test",
                BaseUrl = "https://api.openai.com/v1",
                Model = "gpt-4o",
                ApiKey = "sk-test",
                Quirks = new BackendQuirks { SupportsParallelToolCalls = true }
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

            Assert.True(parsed!["parallel_tool_calls"]!.GetValue<bool>());
        }

        #endregion
    }
}
