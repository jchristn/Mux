namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="WebRetrieveTool"/> input validation (no browser launch). Ported
    /// from the <c>WebRetrieveToolTests</c> xUnit suite.
    /// </summary>
    public static class WebRetrieveToolSuite
    {
        /// <summary>
        /// Builds the web-retrieve-tool suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the web-retrieve-tool cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "WebRetrieveTool",
                "Web-retrieve tool definition and validation",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(
                        "WebRetrieveTool",
                        "ToolDefinitionUsesExpectedNameAndSchema",
                        "The tool exposes the expected name and required URL parameter",
                        (CancellationToken ct) =>
                        {
                            WebRetrieveTool tool = new WebRetrieveTool();
                            MuxAssert.AreEqual("web_retrieve", tool.Name, "name");
                            string schema = JsonSerializer.Serialize(tool.ParametersSchema);
                            MuxAssert.Contains("url", schema, "url");
                            MuxAssert.Contains("browser", schema, "browser");
                            MuxAssert.Contains("wait_until", schema, "wait_until");
                            MuxAssert.Contains("\"required\":[\"url\"]", schema, "required url");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor(
                        "WebRetrieveTool",
                        "MissingUrlReturnsError",
                        "A missing URL is rejected before browser launch",
                        async (CancellationToken ct) =>
                        {
                            WebRetrieveTool tool = new WebRetrieveTool();
                            ToolResult result = await tool.ExecuteAsync("call0", ToolArgs.From(new { browser = "chromium" }), ".", ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(result.Success, "failure");
                            MuxAssert.Contains("web_retrieve_error", result.Content, "error code");
                            MuxAssert.Contains("url", result.Content, "url mention");
                        }),

                    new TestCaseDescriptor(
                        "WebRetrieveTool",
                        "NonHttpUrlReturnsError",
                        "Non-HTTP URLs are rejected before browser launch",
                        async (CancellationToken ct) =>
                        {
                            WebRetrieveTool tool = new WebRetrieveTool();
                            ToolResult result = await tool.ExecuteAsync("call1", ToolArgs.From(new { url = "file:///tmp/example.html" }), ".", ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(result.Success, "failure");
                            MuxAssert.Contains("web_retrieve_error", result.Content, "error code");
                            MuxAssert.Contains("absolute HTTP or HTTPS URL", result.Content, "message");
                        }),

                    new TestCaseDescriptor(
                        "WebRetrieveTool",
                        "UnsupportedBrowserReturnsError",
                        "Unsupported browser names are rejected before browser launch",
                        async (CancellationToken ct) =>
                        {
                            WebRetrieveTool tool = new WebRetrieveTool();
                            ToolResult result = await tool.ExecuteAsync("call2", ToolArgs.From(new { url = "https://example.com", browser = "webkit" }), ".", ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(result.Success, "failure");
                            MuxAssert.Contains("web_retrieve_error", result.Content, "error code");
                        }),

                    new TestCaseDescriptor(
                        "WebRetrieveTool",
                        "UnsupportedWaitUntilReturnsError",
                        "Unsupported wait states are rejected before browser launch",
                        async (CancellationToken ct) =>
                        {
                            WebRetrieveTool tool = new WebRetrieveTool();
                            ToolResult result = await tool.ExecuteAsync("call3", ToolArgs.From(new { url = "https://example.com", wait_until = "settled" }), ".", ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(result.Success, "failure");
                            MuxAssert.Contains("web_retrieve_error", result.Content, "error code");
                            MuxAssert.Contains("wait_until", result.Content, "wait_until mention");
                        })
                });
        }
    }
}
