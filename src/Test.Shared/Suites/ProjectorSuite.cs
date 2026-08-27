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

                    Case("ThinkingRendersAndStaysOutOfHistory", "Thinking renders as a labeled block and is excluded from captured assistant text", async (CancellationToken ct) =>
                    {
                        Pane pane = new Pane("t");
                        AgentEventProjector projector = new AgentEventProjector(pane);
                        await projector.ProjectAsync(Script(new AgentEvent[] { Thinking("Let me think."), Text("the answer"), Completed() }, ct), ct).ConfigureAwait(false);

                        IReadOnlyList<string> lines = pane.SnapshotPlainLines();
                        string joined = Join(lines);
                        MuxAssert.Contains("💭 thinking", joined, "thinking header rendered");
                        MuxAssert.Contains("the answer", joined, "answer rendered");

                        // Thinking body is not indented.
                        MuxAssert.IsTrue(lines.Any(l => l == "Let me think."), "thinking body renders without indentation");

                        // A blank line separates the thinking block from the answer.
                        int thinkingIdx = IndexOfContaining(lines, "Let me think.");
                        int answerIdx = IndexOfContaining(lines, "the answer");
                        MuxAssert.IsTrue(thinkingIdx >= 0 && answerIdx > thinkingIdx, "the answer follows the thinking block");
                        bool blankBetween = false;
                        for (int i = thinkingIdx + 1; i < answerIdx; i++)
                        {
                            if (string.IsNullOrEmpty(lines[i])) blankBetween = true;
                        }
                        MuxAssert.IsTrue(blankBetween, "a blank line separates thinking from the answer");

                        MuxAssert.AreEqual("the answer", projector.CapturedAssistantText, "captured assistant text (history) excludes thinking");
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

                    Case("TaskPlanRendersChecklistBlock", "A task plan renders a header and one line per task", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, PlanEvent(
                            Mux.Core.Enums.TaskPlanChangeKindEnum.PlanCreated,
                            TaskItem("t1", "Study the pattern", Mux.Core.Enums.AgentTaskStatusEnum.Pending),
                            TaskItem("t2", "Add the interface", Mux.Core.Enums.AgentTaskStatusEnum.Pending)), Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("Tasks 0/2", joined, "header");
                        MuxAssert.Contains("Study the pattern", joined, "task 1");
                        MuxAssert.Contains("Add the interface", joined, "task 2");
                        MuxAssert.Contains("◻", joined, "pending glyph");
                    }),

                    Case("TaskStatusUpdatesInPlace", "A later status change rewrites the task line, not appends", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct,
                            PlanEvent(Mux.Core.Enums.TaskPlanChangeKindEnum.PlanCreated,
                                TaskItem("t1", "Study the pattern", Mux.Core.Enums.AgentTaskStatusEnum.InProgress)),
                            PlanEvent(Mux.Core.Enums.TaskPlanChangeKindEnum.TaskStatusChanged,
                                TaskItem("t1", "Study the pattern", Mux.Core.Enums.AgentTaskStatusEnum.Completed)),
                            Completed());
                        MuxAssert.AreEqual(1, CountContaining(lines, "Study the pattern"), "single task line (updated in place)");
                        string joined = Join(lines);
                        MuxAssert.Contains("✔", joined, "completed glyph");
                        MuxAssert.Contains("Tasks 1/1", joined, "header updated");
                    }),

                    Case("ErrorRendersErrorLine", "An error event renders a code/message line", async (CancellationToken ct) =>
                    {
                        IReadOnlyList<string> lines = await ProjectAsync(ct, new ErrorEvent { Code = "llm_error", Message = "kaboom" }, Completed());
                        string joined = Join(lines);
                        MuxAssert.Contains("Error [llm_error]", joined, "error code");
                        MuxAssert.Contains("kaboom", joined, "error message");
                    }),

                    Case("SidebarShowsTaskProgress", "The sidebar shows a TASKS n/m row when the plan has tasks", async (CancellationToken ct) =>
                    {
                        Pane pane = new Pane("s");
                        new SidebarView(pane).Refresh("model", new ConversationStats { TaskTotal = 3, TaskCompleted = 1 });
                        string text = Join(pane.SnapshotPlainLines());
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxAssert.Contains("TASKS", text, "tasks row");
                        MuxAssert.Contains("1/3", text, "progress");
                    }),

                    Case("SidebarHidesTasksWhenNoPlan", "The sidebar omits the TASKS row when there is no plan", async (CancellationToken ct) =>
                    {
                        Pane pane = new Pane("s");
                        new SidebarView(pane).Refresh("model", new ConversationStats { TaskTotal = 0 });
                        string text = Join(pane.SnapshotPlainLines());
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxAssert.IsFalse(text.Contains("TASKS", StringComparison.Ordinal), "no tasks row");
                    }),

                    Case("HeartbeatReArmsWaitStateBetweenToolBatches", "The model-working signal fires on each heartbeat and re-arms model-responded for the next step", async (CancellationToken ct) =>
                    {
                        Pane pane = new Pane("t");
                        AgentEventProjector projector = new AgentEventProjector(pane);
                        int responded = 0;
                        int working = 0;
                        projector.ModelResponded += () => responded++;
                        projector.ModelWorking += () => working++;

                        // A two-step turn: text + tool call, heartbeat, then a second text + tool call,
                        // heartbeat, then completion. Each step's first output should re-fire ModelResponded,
                        // and each heartbeat should fire ModelWorking so the shell can resume its indicator.
                        await projector.ProjectAsync(
                            Script(
                                new AgentEvent[]
                                {
                                    Text("first"),
                                    Proposed("t1", "list_directory"),
                                    CompletedTool("t1", "list_directory", true, 1),
                                    Heartbeat(1),
                                    Text("second"),
                                    Proposed("t2", "list_directory"),
                                    CompletedTool("t2", "list_directory", true, 1),
                                    Heartbeat(2),
                                    Completed()
                                },
                                ct),
                            ct).ConfigureAwait(false);

                        MuxAssert.AreEqual(2, working, "ModelWorking fired once per heartbeat");
                        MuxAssert.AreEqual(2, responded, "ModelResponded re-armed and fired once per step");
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

        private static AssistantThinkingEvent Thinking(string text)
        {
            return new AssistantThinkingEvent { Text = text };
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

        private static HeartbeatEvent Heartbeat(int step)
        {
            return new HeartbeatEvent { StepNumber = step };
        }

        private static RunCompletedEvent Completed()
        {
            return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static Mux.Core.Tasks.AgentTask TaskItem(string id, string title, Mux.Core.Enums.AgentTaskStatusEnum status)
        {
            return new Mux.Core.Tasks.AgentTask { Id = id, Title = title, Status = status };
        }

        private static TaskPlanUpdatedEvent PlanEvent(Mux.Core.Enums.TaskPlanChangeKindEnum changeKind, params Mux.Core.Tasks.AgentTask[] tasks)
        {
            return new TaskPlanUpdatedEvent { ChangeKind = changeKind, Tasks = new List<Mux.Core.Tasks.AgentTask>(tasks) };
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static int CountContaining(IReadOnlyList<string> lines, string needle)
        {
            return lines.Count(line => line.Contains(needle, StringComparison.Ordinal));
        }

        private static int IndexOfContaining(IReadOnlyList<string> lines, string needle)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains(needle, StringComparison.Ordinal)) return i;
            }

            return -1;
        }

        #endregion
    }
}
