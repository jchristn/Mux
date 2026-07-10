namespace Test.Xunit.Rendering
{
    using global::Xunit;
    using Mux.Cli.Rendering;

    /// <summary>
    /// Unit tests for tool call summary rendering.
    /// </summary>
    public class ToolCallRendererTests
    {
        #region Public-Methods

        /// <summary>
        /// Verifies that web_retrieve approval prompts summarize the requested URL.
        /// </summary>
        [Fact]
        public void FormatToolSummary_WebRetrieve_ReturnsUrl()
        {
            string summary = ToolCallRenderer.FormatToolSummary("web_retrieve", "{\"url\":\"https://example.com/docs\"}");

            Assert.Equal("web_retrieve: https://example.com/docs", summary);
        }

        /// <summary>
        /// Verifies that tool call lifecycle lines use the standard box-drawing prefix.
        /// </summary>
        [Fact]
        public void FormatToolCallLine_AddsToolLogPrefix()
        {
            string line = ToolCallRenderer.FormatToolCallLine("write_file: src/Program.cs");

            Assert.Equal("  \u251C Tool call: write_file: src/Program.cs", line);
        }

        /// <summary>
        /// Verifies that tool execution lifecycle lines use the standard box-drawing prefix.
        /// </summary>
        [Fact]
        public void FormatToolExecutionLine_AddsToolLogPrefix()
        {
            string line = ToolCallRenderer.FormatToolExecutionLine("write_file", "Program.cs", "ok", 42);

            Assert.Equal("  \u2514 Tool write_file: Program.cs ok 42ms", line);
        }

        #endregion
    }
}
