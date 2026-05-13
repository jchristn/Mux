namespace Test.Xunit.Tools
{
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;

    /// <summary>
    /// Unit tests for the <see cref="WebRetrieveTool"/> class.
    /// </summary>
    public class WebRetrieveToolTests
    {
        #region Public-Methods

        /// <summary>
        /// Verifies that the tool exposes the expected name and required URL parameter.
        /// </summary>
        [Fact]
        public void ToolDefinition_UsesExpectedNameAndSchema()
        {
            WebRetrieveTool tool = new WebRetrieveTool();

            Assert.Equal("web_retrieve", tool.Name);
            string schema = JsonSerializer.Serialize(tool.ParametersSchema);
            Assert.Contains("url", schema);
            Assert.Contains("browser", schema);
            Assert.Contains("wait_until", schema);
            Assert.Contains("\"required\":[\"url\"]", schema);
        }

        /// <summary>
        /// Verifies that a missing URL is rejected before browser launch.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WithMissingUrl_ReturnsError()
        {
            WebRetrieveTool tool = new WebRetrieveTool();
            JsonElement args = JsonSerializer.SerializeToElement(new { browser = "chromium" });

            ToolResult result = await tool.ExecuteAsync("call0", args, ".", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("web_retrieve_error", result.Content);
            Assert.Contains("url", result.Content);
        }

        /// <summary>
        /// Verifies that non-HTTP URLs are rejected before browser launch.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WithNonHttpUrl_ReturnsError()
        {
            WebRetrieveTool tool = new WebRetrieveTool();
            JsonElement args = JsonSerializer.SerializeToElement(new { url = "file:///tmp/example.html" });

            ToolResult result = await tool.ExecuteAsync("call1", args, ".", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("web_retrieve_error", result.Content);
            Assert.Contains("absolute HTTP or HTTPS URL", result.Content);
        }

        /// <summary>
        /// Verifies that unsupported browser names are rejected before browser launch.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WithUnsupportedBrowser_ReturnsError()
        {
            WebRetrieveTool tool = new WebRetrieveTool();
            JsonElement args = JsonSerializer.SerializeToElement(new { url = "https://example.com", browser = "webkit" });

            ToolResult result = await tool.ExecuteAsync("call2", args, ".", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("web_retrieve_error", result.Content);
        }

        /// <summary>
        /// Verifies that unsupported wait states are rejected before browser launch.
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_WithUnsupportedWaitUntil_ReturnsError()
        {
            WebRetrieveTool tool = new WebRetrieveTool();
            JsonElement args = JsonSerializer.SerializeToElement(new { url = "https://example.com", wait_until = "settled" });

            ToolResult result = await tool.ExecuteAsync("call3", args, ".", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Contains("web_retrieve_error", result.Content);
            Assert.Contains("wait_until", result.Content);
        }

        #endregion
    }
}
