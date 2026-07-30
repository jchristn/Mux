namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the TUIKit-hosted interactive shell (<see cref="MuxTuiApp"/>). Every case
    /// drives the shell through an injected <see cref="HeadlessBackend"/> and a fake job runner, so no
    /// terminal, network, or model is required.
    /// </summary>
    public static class TuiShellSuite
    {
        private const string SuiteId = "TuiShell";

        /// <summary>
        /// Builds the shell suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for interactive-shell cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "TUIKit interactive shell boot, input, submit, and event projection",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "BootWritesHeaderAndFooter", "Boot renders the session header and footer hints", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(new HeadlessBackend(80, 24), manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            string footer = Join(app.FooterSnapshot());

                            MuxAssert.Contains("mux", transcript, "header brand");
                            MuxAssert.Contains("demo", transcript, "header title");
                            MuxAssert.Contains("Type a prompt", transcript, "header hint");
                            MuxAssert.Contains("^Q", footer, "footer quit hint");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "RenderProducesOutput", "Starting and rendering emits output to the backend", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            app.Start();
                            app.RenderOnce();

                            MuxAssert.IsTrue(backend.PeekOutput().Length > 0, "rendered output present");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "TypingUpdatesComposer", "Typed characters accumulate in the composer", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("hello");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual("hello", app.ComposerText, "composer text");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job before submit");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EnterSubmitsPromptAndClearsComposer", "Enter submits the composer text as a job and clears the composer", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hello\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(1, manager.Jobs.Count, "one job submitted");
                            MuxAssert.AreEqual("hello", manager.Jobs[0].Prompt, "submitted prompt");
                            MuxAssert.AreEqual(string.Empty, app.ComposerText, "composer cleared");

                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EmptyEnterDoesNotSubmit", "Enter with an empty composer submits nothing", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job submitted");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "WhitespaceEnterDoesNotSubmit", "Enter with only whitespace submits nothing", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            backend.FeedInput("   \r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job submitted");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "AssistantTextProjectedToTranscript", "Submitting echoes the prompt and projects assistant text", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hi\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("hi", transcript, "user prompt echoed");
                            MuxAssert.Contains("Echo: hi", transcript, "assistant text projected");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ToolEventsProjectedToTranscript", "Tool proposed and completed events render to the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(ToolRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("do it\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("read_file", transcript, "tool name projected");
                            MuxAssert.Contains("✓", transcript, "tool success mark projected");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "ErrorEventProjectedToTranscript", "Error events render to the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(ErrorRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("boom\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("Error [llm_error]", transcript, "error code projected");
                            MuxAssert.Contains("kaboom", transcript, "error message projected");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "CtrlLClearsTranscript", "Ctrl+L clears the transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.TranscriptSnapshot().Count > 0, "transcript seeded");

                            backend.FeedInput("\f"); // Ctrl+L
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(0, app.TranscriptSnapshot().Count, "transcript cleared");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "EscapeCancelsFocusedJob", "Escape cancels the focused running job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(BlockingRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("run\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(1, manager.Jobs.Count, "job submitted");
                            Job job = manager.Jobs[0];
                            await WaitForStateAsync(job, JobState.Running, ct).ConfigureAwait(false);

                            backend.FeedInput(new byte[] { 0x1b }); // Escape byte, buffered until flush
                            app.PumpInputOnce(); // feeds ESC into the parser
                            app.PumpInputOnce(); // idle pump flushes the lone ESC to an Escape key

                            await WaitForStateAsync(job, JobState.Cancelled, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(JobState.Cancelled, job.State, "job cancelled");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "CtrlQStopsRunLoop", "Ctrl+Q exits the run loop", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            Task run = app.RunAsync(ct);
                            backend.FeedInput(new byte[] { 0x11 }); // Ctrl+Q -> quit-confirmation modal
                            await WaitUntilAsync(() => app.IsModalActive, ct).ConfigureAwait(false);
                            backend.FeedInput("\r"); // confirm "Quit" (default button)

                            await run.WaitAsync(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(run.IsCompletedSuccessfully, "run loop exited");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "HomePaneShownBeforeAnyJob", "Before any job the home pane is focused", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.FocusedJobId == null, "no focused job");
                            MuxAssert.AreEqual(0, app.JobIds.Count, "no jobs");
                            MuxAssert.Contains("mux", Join(app.TranscriptSnapshot()), "home header");
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SubmitStartsATurnInTheConversation", "Submitting starts a turn and echoes into the single conversation", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("hi\r");
                            app.PumpInputOnce();

                            MuxAssert.AreEqual(1, app.JobIds.Count, "one turn started");
                            string jobId = app.JobIds[0];
                            MuxAssert.AreEqual(jobId, app.FocusedJobId, "turn is current");

                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("mux> hi", transcript, "prompt echoed with mux> prefix");

                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    new TestCaseDescriptor(SuiteId, "SequentialTurnsShareOneTranscript", "Both turns accumulate into the single conversation transcript", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(80, 24);
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend, manager, "demo"))
                        {
                            backend.FeedInput("one\r");
                            app.PumpInputOnce();
                            backend.FeedInput("two\r");
                            app.PumpInputOnce();
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            MuxAssert.AreEqual(2, app.JobIds.Count, "two turns ran");
                            string transcript = Join(app.TranscriptSnapshot());
                            MuxAssert.Contains("Echo: one", transcript, "first turn output present");
                            MuxAssert.Contains("Echo: two", transcript, "second turn output present");
                            MuxAssert.Contains("one", transcript, "first prompt echoed");
                            MuxAssert.Contains("two", transcript, "second prompt echoed");
                        }
                    })
                });
        }

        #region Fake-Runners

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> ToolRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new ToolCallProposedEvent
            {
                ToolCall = new ToolCall { Id = "t1", Name = "read_file", Arguments = "{}" }
            };
            yield return new ToolCallCompletedEvent
            {
                ToolCallId = "t1",
                ToolName = "read_file",
                Result = new ToolResult { Success = true, Content = "ok" },
                ElapsedMs = 5
            };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> ErrorRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new ErrorEvent { Code = "llm_error", Message = "kaboom" };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> BlockingRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            yield return CompletedEvent();
        }

        #endregion

        #region Helpers

        private static JobManager NewManager(Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> runner)
        {
            return new JobManager(runner, maxConcurrency: 1);
        }

        private static MuxTuiApp NewApp(HeadlessBackend backend, JobManager manager, string title)
        {
            MuxTuiApp app = new MuxTuiApp(backend, manager, title, ApprovalPolicyEnum.AutoApprove);
            // These cases exercise panes/focus, not the submit chooser; make submit deterministic.
            return app;
        }

        private static RunCompletedEvent CompletedEvent()
        {
            return new RunCompletedEvent
            {
                RunId = Guid.NewGuid().ToString("N"),
                Status = "completed",
                IterationsCompleted = 1,
                DurationMs = 1
            };
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!condition())
                {
                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }
            }
        }

        private static async Task WaitForStateAsync(Job job, JobState expectedState, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    if (job.State == expectedState)
                    {
                        return;
                    }

                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }

                linked.Token.ThrowIfCancellationRequested();
            }
        }

        #endregion
    }
}
