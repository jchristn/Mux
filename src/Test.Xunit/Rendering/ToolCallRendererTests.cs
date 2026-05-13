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

        #endregion
    }
}
