namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Touchstone.Core;
    using TUIKit.Content;

    /// <summary>
    /// Touchstone suite for <see cref="AgentEventProjector"/>. Each case scripts an
    /// <see cref="AgentEvent"/> sequence, projects it onto a bare <see cref="Pane"/>, and asserts the
    /// resulting plain-text lines — covering markdown finalization, in-place tool-status updates, block
    /// interleaving, errors, and cancellation.
    /// </summary>
    public static class ProjectorSuite
    {
        private const string SuiteId = "Projector";

        /// <summary>
        /// Builds the projector suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for projector cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "AgentEvent projection to a transcript pane",
                new List<TestCaseDescriptor>
                {
                    Case("PlainAssistantTextFinalizes", "A plain assistant text block renders its text", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Text("hello "), Text("world"), Completed());
                        MuxAssert.Contains("hello world", Join(lines), "assistant text");
                    }),

                    Case("AssistantMarkdownBulletsTransformed", "Markdown bullets render with bullet glyphs, not raw dashes", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Text("- alpha\n- beta"), Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("• alpha", joined, "first bullet");
                        MuxAssert.Contains("• beta", joined, "second bullet");
                        MuxAssert.IsFalse(joined.Contains("- alpha", StringComparison.Ordinal), "raw dash removed");
                    }),

                    Case("AssistantMarkdownCodeFenceStripped", "Fenced code renders without the ``` fences", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Text("```\ncode line\n```"), Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("code line", joined, "code content");
                        MuxAssert.IsFalse(joined.Contains("```", StringComparison.Ordinal), "fence removed");
                    }),

                    Case("AssistantMultiLineFinalizesAllLines", "A multi-line paragraph keeps every line", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Text("line one\nline two\nline three"), Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("line one", joined, "line 1");
                        MuxAssert.Contains("line two", joined, "line 2");
                        MuxAssert.Contains("line three", joined, "line 3");
                    }),

                    Case("ToolCallUpdatesSingleLineInPlace", "A tool call renders as one line updated from running to done", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Proposed("t1", "read_file"), CompletedTool("t1", "read_file", true, 12), Completed());
                        MuxAssert.AreEqual(1, CountContaining(lines, "read_file"), "single tool line");
                        string joined = Join(lines);
                        MuxAssert.Contains("✓", joined, "success mark");
                        MuxAssert.Contains("12 ms", joined, "elapsed");
                        MuxAssert.IsFalse(joined.Contains("running", StringComparison.Ordinal), "running replaced");
                    }),

                    Case("FailedToolShowsCrossMark", "A failed tool renders a cross mark", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Proposed("t1", "write_file"), CompletedTool("t1", "write_file", false, 3), Completed());
                        MuxAssert.AreEqual(1, CountContaining(lines, "write_file"), "single tool line");
                        MuxAssert.Contains("✗", Join(lines), "failure mark");
                    }),

                    Case("OrphanCompletedToolWritesOwnLine", "A completed event without a prior proposal still renders", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, CompletedTool("t9", "glob", true, 1), Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("glob", joined, "tool name");
                        MuxAssert.Contains("✓", joined, "success mark");
                    }),

                    Case("ToolCallInterruptsAssistantBlock", "Text before and after a tool call becomes two ordered blocks", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(
                            ct,
                            Text("thinking"),
                            Proposed("t1", "read_file"),
                            CompletedTool("t1", "read_file", true, 5),
                            Text("done"),
                            Completed());
                        string joined = Join(lines);
                        int thinking = joined.IndexOf("thinking", StringComparison.Ordinal);
                        int tool = joined.IndexOf("read_file", StringComparison.Ordinal);
                        int done = joined.IndexOf("done", StringComparison.Ordinal);
                        MuxAssert.IsTrue(thinking >= 0 && tool > thinking && done > tool, "ordered thinking < tool < done");
                    }),

                    Case("MultipleAssistantBlocksAllFinalize", "Text split across three blocks all renders", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(
                            ct,
                            Text("aaa"),
                            Proposed("t1", "read_file"),
                            CompletedTool("t1", "read_file", true, 1),
                            Text("bbb"),
                            Proposed("t2", "grep"),
                            CompletedTool("t2", "grep", true, 1),
                            Text("ccc"),
                            Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("aaa", joined, "block a");
                        MuxAssert.Contains("bbb", joined, "block b");
                        MuxAssert.Contains("ccc", joined, "block c");
                    }),

                    Case("ErrorRendersErrorLine", "An error event renders a code/message line", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, new ErrorEvent { Code = "llm_error", Message = "kaboom" }, Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("Error [llm_error]", joined, "error code");
                        MuxAssert.Contains("kaboom", joined, "error message");
                    }),

                    Case("EmptyStreamProducesNoLines", "A stream with only run completion writes nothing", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, Completed());
                        MuxAssert.AreEqual(0, lines.Count, "no lines");
                    }),

                    Case("CancellationWritesNotice", "A cancelled stream writes a cancellation notice", async (CancellationToken ct) =>
                    {
                        Pane pane = new Pane("t");
                        using (CancellationTokenSource cts = new CancellationTokenSource())
                        {
                            cts.Cancel();
                            await new AgentEventProjector(pane).ProjectAsync(Script(new AgentEvent[] { Text("never") }, cts.Token), cts.Token).ConfigureAwait(false);
                        }

                        MuxAssert.Contains("(cancelled)", Join(pane.SnapshotPlainLines()), "cancel notice");
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task<IReadOnlyList<string>> ProjectAsync(CancellationToken ct, params AgentEvent[] events)
        {
            Pane pane = new Pane("t");
            await new AgentEventProjector(pane).ProjectAsync(Script(events, ct), ct).ConfigureAwait(false);
            return pane.SnapshotPlainLines();
        }

        private static async IAsyncEnumerable<AgentEvent> Script(AgentEvent[] events, [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (AgentEvent agentEvent in events)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return agentEvent;
            }
        }

        private static AssistantTextEvent Text(string text)
        {
            return new AssistantTextEvent { Text = text };
        }

        private static ToolCallProposedEvent Proposed(string id, string name)
        {
            return new ToolCallProposedEvent { ToolCall = new ToolCall { Id = id, Name = name, Arguments = "{}" } };
        }

        private static ToolCallCompletedEvent CompletedTool(string id, string name, bool success, long elapsedMs)
        {
            return new ToolCallCompletedEvent
            {
                ToolCallId = id,
                ToolName = name,
                Result = new ToolResult { ToolCallId = id, Success = success, Content = success ? "ok" : "err" },
                ElapsedMs = elapsedMs
            };
        }

        private static RunCompletedEvent Completed()
        {
            return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static int CountContaining(IReadOnlyList<string> lines, string needle)
        {
            return lines.Count(line => line.Contains(needle, StringComparison.Ordinal));
        }

        #endregion
    }
}
