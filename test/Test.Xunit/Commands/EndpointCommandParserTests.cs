namespace Test.Xunit.Commands
{
    using global::Xunit;
    using Mux.Cli.Commands;

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
        /// Verifies that add starts the guided endpoint creation workflow.
        /// </summary>
        [Fact]
        public void Parse_AddCommand_ReturnsAddAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("add");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Add, result.Request!.Action);
            Assert.Null(result.Request.Name);
        }

        /// <summary>
        /// Verifies that add may optionally seed the endpoint name for the wizard.
        /// </summary>
        [Fact]
        public void Parse_AddCommand_WithSeedName_ReturnsAddAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("add openai-prod");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Add, result.Request!.Action);
            Assert.Equal("openai-prod", result.Request.Name);
        }

        /// <summary>
        /// Verifies that edit targets a configured endpoint name.
        /// </summary>
        [Fact]
        public void Parse_EditCommand_ReturnsEditAction()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("edit openai-prod");

            Assert.True(result.Success);
            Assert.NotNull(result.Request);
            Assert.Equal(EndpointCommandAction.Edit, result.Request!.Action);
            Assert.Equal("openai-prod", result.Request.Name);
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

        /// <summary>
        /// Verifies that add rejects unsupported trailing arguments.
        /// </summary>
        [Fact]
        public void Parse_AddCommand_WithUnexpectedArguments_ReturnsError()
        {
            EndpointCommandParseResult result = EndpointCommandParser.Parse("add one two");

            Assert.False(result.Success);
            Assert.Contains("Usage: /endpoint add", result.ErrorMessage);
        }
    }
}
