namespace Test.Xunit.Commands
{
    using global::Xunit;
    using Mux.Cli.Commands;
    using Mux.Core.Enums;

    /// <summary>
    /// Unit tests for interactive endpoint command parsing.
    /// </summary>
    public class EndpointCommandParserTests
    {
        /// <summary>
        /// Verifies that an empty endpoint argument lists configured endpoints.
        /// </summary>
        [Fact]
        public void Parse_EmptyArgument_ReturnsListAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse(string.Empty);

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.List, result.Request!.Action);
        }

        /// <summary>
        /// Verifies that a bare endpoint name is treated as a switch request.
        /// </summary>
        [Fact]
        public void Parse_BareName_ReturnsSwitchAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("openai-prod");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Switch, result.Request!.Action);
            Assert.Equal("openai-prod", result.Request.Name);
        }

        /// <summary>
        /// Verifies that the show subcommand targets the requested endpoint.
        /// </summary>
        [Fact]
        public void Parse_ShowCommand_ReturnsShowAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("show local-dev");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Show, result.Request!.Action);
            Assert.Equal("local-dev", result.Request.Name);
        }

        /// <summary>
        /// Verifies that add supports explicit options and quoted header values.
        /// </summary>
        [Fact]
        public void Parse_AddCommand_ParsesEndpointDetails()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse(
                "add openai-prod --adapter openai-compatible --base-url https://api.example.com/v1 --model gpt-4.1 --default --temperature 0.2 --max-tokens 4096 --context-window 128000 --timeout-ms 20000 --header \"Authorization=Bearer test token\"");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Add, result.Request!.Action);
            Assert.NotNull(result.Request.Endpoint);
            Assert.Equal("openai-prod", result.Request.Endpoint!.Name);
            Assert.Equal(AdapterTypeEnum.OpenAiCompatible, result.Request.Endpoint.AdapterType);
            Assert.Equal("https://api.example.com/v1", result.Request.Endpoint.BaseUrl);
            Assert.Equal("gpt-4.1", result.Request.Endpoint.Model);
            Assert.True(result.Request.Endpoint.IsDefault);
            Assert.Equal(0.2, result.Request.Endpoint.Temperature);
            Assert.Equal(4096, result.Request.Endpoint.MaxTokens);
            Assert.Equal(128000, result.Request.Endpoint.ContextWindow);
            Assert.Equal(20000, result.Request.Endpoint.TimeoutMs);
            Assert.Equal("Bearer test token", result.Request.Endpoint.Headers["Authorization"]);
        }

        /// <summary>
        /// Verifies that add rejects incomplete endpoint definitions.
        /// </summary>
        [Fact]
        public void Parse_AddCommand_MissingRequiredFields_ReturnsError()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse(
                "add incomplete --adapter openai-compatible --model gpt-4.1");

            Assert.False(result.Success);
            Assert.Contains("Missing required endpoint fields", result.ErrorMessage);
        }

        /// <summary>
        /// Verifies that remove targets a configured endpoint name.
        /// </summary>
        [Fact]
        public void Parse_RemoveCommand_ReturnsRemoveAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("remove stale-endpoint");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Remove, result.Request!.Action);
            Assert.Equal("stale-endpoint", result.Request.Name);
        }
    }
}
