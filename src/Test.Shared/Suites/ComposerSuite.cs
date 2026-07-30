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
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the composer, prompt history, and the enqueue-while-busy submit chooser.
    /// Covers <see cref="PromptHistory"/> in isolation plus the shell's newline / submit / chooser /
    /// history / slash-routing gestures driven through the real input path.
    /// </summary>
    public static class ComposerSuite
    {
        private const string SuiteId = "Composer";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the composer suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for composer cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Composer, prompt history, and submit chooser",
                new List<TestCaseDescriptor>
                {
                    // ---- PromptHistory unit cases ----
                    Case("HistoryRecallsInReverseOrder", "Previous walks from newest to oldest", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("b");
                        history.Add("c");

                        MuxAssert.IsTrue(history.TryPrevious(out string p1), "prev 1"); MuxAssert.AreEqual("c", p1, "newest");
                        MuxAssert.IsTrue(history.TryPrevious(out string p2), "prev 2"); MuxAssert.AreEqual("b", p2, "middle");
                        MuxAssert.IsTrue(history.TryPrevious(out string p3), "prev 3"); MuxAssert.AreEqual("a", p3, "oldest");
                        MuxAssert.IsTrue(history.TryPrevious(out string p4), "prev 4"); MuxAssert.AreEqual("a", p4, "clamped at oldest");
                        return Task.CompletedTask;
                    }),

                    Case("HistoryNextReturnsToFreshDraft", "Next walks back to an empty fresh draft", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("b");
                        history.TryPrevious(out _); // b
                        history.TryPrevious(out _); // a

                        MuxAssert.IsTrue(history.TryNext(out string n1), "next 1"); MuxAssert.AreEqual("b", n1, "back to b");
                        MuxAssert.IsTrue(history.TryNext(out string n2), "next 2"); MuxAssert.AreEqual(string.Empty, n2, "fresh draft");
                        MuxAssert.IsFalse(history.TryNext(out _), "no move past fresh");
                        return Task.CompletedTask;
                    }),

                    Case("HistoryIgnoresBlankAndConsecutiveDuplicates", "Blank and repeated entries are not stored", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        history.Add("a");
                        history.Add("a");
                        history.Add("   ");
                        history.Add("b");
                        MuxAssert.AreEqual(2, history.Count, "deduped count");
                        return Task.CompletedTask;
                    }),

                    Case("HistoryEmptyPreviousReturnsFalse", "Previous on empty history returns false", (CancellationToken ct) =>
                    {
                        PromptHistory history = new PromptHistory();
                        MuxAssert.IsFalse(history.TryPrevious(out _), "no history");
                        return Task.CompletedTask;
                    }),

                    // ---- Composer newline gestures ----
                    Case("AltEnterInsertsNewlineNotSubmit", "Alt+Enter inserts a newline instead of submitting", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "line1" + Esc + "\r" + "line2");
                            MuxAssert.AreEqual("line1\nline2", app.ComposerText, "two-line composer");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "not submitted");
                        }
                    }),

                    Case("ShiftEnterInsertsNewline", "Shift+Enter (CSI-u) inserts a newline", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "a" + Esc + "[13;2u" + "b");
                            MuxAssert.AreEqual("a\nb", app.ComposerText, "two-line composer");
                        }
                    }),

                    Case("EnterSubmitsMultiLinePrompt", "Enter submits a multi-line prompt as one job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "line1" + Esc + "\r" + "line2" + "\r");
                            MuxAssert.AreEqual(1, manager.Jobs.Count, "one job");
                            MuxAssert.AreEqual("line1\nline2", manager.Jobs[0].Prompt, "multi-line prompt");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    // ---- Submit chooser ----
                    Case("EnterShowsChooserWhenJobActive", "Enter opens the chooser while a job is active", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);

                            Feed(backend, app, "second" + "\r");
                            MuxAssert.IsTrue(app.IsChooserActive, "chooser open");
                            MuxAssert.AreEqual(1, app.JobIds.Count, "no new job yet");

                            Feed(backend, app, "1"); // new job
                            MuxAssert.IsFalse(app.IsChooserActive, "chooser closed");
                            MuxAssert.AreEqual(2, app.JobIds.Count, "second job created");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("ChooserAddToFocusedAppendsFollowUp", "Choosing add-to-focused appends to the active job", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);
                            string focused = app.JobIds[0];

                            Feed(backend, app, "second" + "\r");
                            Feed(backend, app, "2"); // add to focused

                            MuxAssert.IsFalse(app.IsChooserActive, "chooser closed");
                            MuxAssert.AreEqual(1, app.JobIds.Count, "no new job");
                            MuxAssert.Contains("second", Join(app.JobTranscriptSnapshot(focused)), "follow-up echoed to focused pane");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("ChooserEscapeCancels", "Escape dismisses the chooser without submitting", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);

                            Feed(backend, app, "second" + "\r");
                            MuxAssert.IsTrue(app.IsChooserActive, "chooser open");

                            backend.FeedInput(new byte[] { 0x1b }); // Escape
                            app.PumpInputOnce();
                            app.PumpInputOnce();

                            MuxAssert.IsFalse(app.IsChooserActive, "chooser cancelled");
                            MuxAssert.AreEqual(1, app.JobIds.Count, "no new job");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("ChooserRememberSetsSessionDefault", "Remembering a choice skips the chooser next time", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 4))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);

                            Feed(backend, app, "second" + "\r");
                            Feed(backend, app, "r"); // toggle remember
                            Feed(backend, app, "1"); // new job, remembered
                            MuxAssert.AreEqual(2, app.JobIds.Count, "second created");

                            Feed(backend, app, "third" + "\r");
                            MuxAssert.IsFalse(app.IsChooserActive, "chooser skipped by remembered default");
                            MuxAssert.AreEqual(3, app.JobIds.Count, "third created directly");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    Case("CtrlEnterBypassesChooser", "Ctrl+Enter submits a new job without the chooser", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = NewSignal();
                        await using (JobManager manager = new JobManager(Gated(release), maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend: out HeadlessBackend backend, manager: manager))
                        {
                            Feed(backend, app, "first" + "\r");
                            await WaitForActiveAsync(manager, ct).ConfigureAwait(false);

                            Feed(backend, app, "second" + Esc + "[13;5u"); // Ctrl+Enter
                            MuxAssert.IsFalse(app.IsChooserActive, "no chooser");
                            MuxAssert.AreEqual(2, app.JobIds.Count, "second job created directly");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                        }
                    }),

                    // ---- Slash routing ----
                    Case("SlashInputRoutesToHandler", "A leading slash routes to the slash handler, not a job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            string? captured = null;
                            app.SlashHandler = (string input) => { captured = input; return true; };

                            Feed(backend, app, "/model list" + "\r");
                            await Task.CompletedTask.ConfigureAwait(false);

                            MuxAssert.AreEqual("/model list", captured, "handler received input");
                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job created");
                        }
                    }),

                    Case("UnknownSlashWritesNotice", "An unhandled slash command writes a notice", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "/bogus" + "\r");
                            await Task.CompletedTask.ConfigureAwait(false);

                            MuxAssert.AreEqual(0, manager.Jobs.Count, "no job created");
                            MuxAssert.Contains("Unknown command", Join(app.TranscriptSnapshot()), "notice written");
                        }
                    }),

                    // ---- History recall ----
                    Case("UpArrowRecallsLastPrompt", "Up recalls the last submitted prompt into the composer", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 24);
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Feed(backend, app, "alpha" + "\r");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            MuxAssert.AreEqual(string.Empty, app.ComposerText, "composer cleared after submit");

                            Feed(backend, app, Esc + "[A"); // Up
                            MuxAssert.AreEqual("alpha", app.ComposerText, "recalled last prompt");

                            Feed(backend, app, Esc + "[B"); // Down -> fresh draft
                            MuxAssert.AreEqual(string.Empty, app.ComposerText, "back to fresh draft");
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static JobManager NewManager(Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> runner)
        {
            return new JobManager(runner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(HeadlessBackend backend, JobManager manager)
        {
            // Default (Ask) enqueue behavior so the chooser cases exercise it.
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager)
        {
            backend = new HeadlessBackend(100, 24);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> Gated(TaskCompletionSource<bool> release)
        {
            return (Job job, string prompt, CancellationToken ct) => GatedStream(release, ct);
        }

        private static async IAsyncEnumerable<AgentEvent> GatedStream(TaskCompletionSource<bool> release, [EnumeratorCancellation] CancellationToken ct)
        {
            await release.Task.WaitAsync(ct).ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "done" };
            yield return CompletedEvent();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return CompletedEvent();
        }

        private static RunCompletedEvent CompletedEvent()
        {
            return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static async Task WaitForActiveAsync(JobManager manager, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    foreach (Job job in manager.Jobs)
                    {
                        if (job.State == JobState.Running)
                        {
                            return;
                        }
                    }

                    await Task.Delay(10, linked.Token).ConfigureAwait(false);
                }

                linked.Token.ThrowIfCancellationRequested();
            }
        }

        #endregion
    }
}
