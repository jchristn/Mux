namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SessionExporter"/>: the local, server-free session-sharing tier.
    /// Verifies the Markdown and HTML renderings carry session metadata and conversation content, that HTML
    /// output is self-contained and escapes dynamic text, that tool calls render, and that the format
    /// helpers behave.
    /// </summary>
    public static class SessionExporterSuite
    {
        private const string SuiteId = "SessionExporter";

        /// <summary>
        /// Builds the session-exporter suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the exporter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Local session export to Markdown and HTML",
                new List<TestCaseDescriptor>
                {
                    Case("MarkdownIncludesMetadataAndContent", "Markdown export carries metadata and conversation content", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        string md = SessionExporter.ToMarkdown(SampleSnapshot());
                        MuxAssert.Contains("# Demo Session", md, "title heading");
                        MuxAssert.Contains("ollama-local", md, "endpoint");
                        MuxAssert.Contains("qwen2.5-coder:7b", md, "model");
                        MuxAssert.Contains("Read README.md", md, "user content");
                        MuxAssert.Contains("Here is the summary", md, "assistant content");
                    }),

                    Case("MarkdownRendersToolCalls", "Markdown export renders tool calls in a fenced block", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        string md = SessionExporter.ToMarkdown(SampleSnapshot());
                        MuxAssert.Contains("Tool call:", md, "tool call label");
                        MuxAssert.Contains("read_file", md, "tool name");
                        MuxAssert.Contains("```json", md, "arguments fenced as json");
                    }),

                    Case("HtmlIsSelfContained", "HTML export is a self-contained document with no external requests", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        string html = SessionExporter.ToHtml(SampleSnapshot());
                        MuxAssert.Contains("<!doctype html>", html, "doctype present");
                        MuxAssert.Contains("<style>", html, "inline styles present");
                        MuxAssert.IsFalse(html.Contains("http://", StringComparison.OrdinalIgnoreCase), "no http references");
                        MuxAssert.IsFalse(html.Contains("https://", StringComparison.OrdinalIgnoreCase), "no https references");
                        MuxAssert.IsFalse(html.Contains("<script", StringComparison.OrdinalIgnoreCase), "no scripts");
                    }),

                    Case("HtmlEscapesDynamicText", "HTML export escapes angle brackets in message content", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        SessionSnapshot snapshot = new SessionSnapshot
                        {
                            Id = "s1",
                            Title = "Escaping",
                            ConversationHistory = new List<ConversationMessage>
                            {
                                new ConversationMessage { Role = RoleEnum.User, Content = "Consider <b>bold</b> & \"quotes\"." }
                            }
                        };

                        string html = SessionExporter.ToHtml(snapshot);
                        MuxAssert.Contains("&lt;b&gt;bold&lt;/b&gt;", html, "tags escaped");
                        MuxAssert.Contains("&amp;", html, "ampersand escaped");
                        MuxAssert.IsFalse(html.Contains("<b>bold</b>", StringComparison.Ordinal), "no raw injected markup");
                    }),

                    Case("FormatHelpersNormalizeAndRender", "TryNormalizeFormat and Render accept md and html", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxAssert.IsTrue(SessionExporter.TryNormalizeFormat("markdown", out string mdExt), "markdown recognized");
                        MuxAssert.AreEqual("md", mdExt, "markdown extension");
                        MuxAssert.IsTrue(SessionExporter.TryNormalizeFormat("HTM", out string htmlExt), "htm recognized");
                        MuxAssert.AreEqual("html", htmlExt, "html extension");
                        MuxAssert.IsFalse(SessionExporter.TryNormalizeFormat("pdf", out _), "unknown rejected");

                        string rendered = SessionExporter.Render(SampleSnapshot(), "html");
                        MuxAssert.Contains("<!doctype html>", rendered, "render html dispatches to ToHtml");
                    }),

                    Case("NullSnapshotThrows", "Rendering a null snapshot throws", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        bool threw = false;
                        try
                        {
                            SessionExporter.ToMarkdown(null!);
                        }
                        catch (ArgumentNullException)
                        {
                            threw = true;
                        }

                        MuxAssert.IsTrue(threw, "null snapshot throws ArgumentNullException");
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static SessionSnapshot SampleSnapshot()
        {
            return new SessionSnapshot
            {
                Id = "demo-1",
                Title = "Demo Session",
                EndpointName = "ollama-local",
                Model = "qwen2.5-coder:7b",
                UpdatedUtc = new DateTime(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc),
                ConversationHistory = new List<ConversationMessage>
                {
                    new ConversationMessage { Role = RoleEnum.User, Content = "Read README.md and summarize it." },
                    new ConversationMessage
                    {
                        Role = RoleEnum.Assistant,
                        ToolCalls = new List<ToolCall>
                        {
                            new ToolCall { Id = "c1", Name = "read_file", Arguments = "{\"path\":\"README.md\"}" }
                        }
                    },
                    new ConversationMessage { Role = RoleEnum.Tool, Content = "# mux\nA CLI agent.", ToolCallId = "c1" },
                    new ConversationMessage { Role = RoleEnum.Assistant, Content = "Here is the summary: mux is a CLI agent." }
                }
            };
        }
    }
}
