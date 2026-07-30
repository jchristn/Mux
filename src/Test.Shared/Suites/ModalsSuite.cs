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
    /// Touchstone suite for the M11 modals: the tool-approval modal (which backs the engine's interactive
    /// escalation) and the jobs modal. Exercises positive paths (approve / deny / always / focus) and
    /// negative paths (escape, null tool call, empty job list, app shutdown while awaiting).
    /// </summary>
    public static class ModalsSuite
    {
        private const string SuiteId = "Modals";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the modals suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for modal cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Approval and jobs modals",
                new List<TestCaseDescriptor>
                {
                    // ---- Approval modal (positive) ----
                    Case("ApprovalApproveReturnsY", "Selecting Approve returns the approve response", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Task<string> approval = app.RequestApprovalAsync(Tool("write_file"));
                            MuxAssert.IsTrue(app.IsModalActive, "modal shown");
                            MuxAssert.AreEqual(1, app.ModalCount, "one modal");

                            Feed(backend, app, "\r"); // Enter on option 0
                            MuxAssert.AreEqual("y", await approval.ConfigureAwait(false), "approve -> y");
                            MuxAssert.IsFalse(app.IsModalActive, "modal dismissed");
                        }
                    }),

                    Case("ApprovalDenyReturnsN", "Selecting Deny returns the deny response", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Task<string> approval = app.RequestApprovalAsync(Tool("delete_file"));
                            Feed(backend, app, Esc + "[B"); // Down -> option 1 (Deny)
                            Feed(backend, app, "\r");
                            MuxAssert.AreEqual("n", await approval.ConfigureAwait(false), "deny -> n");
                        }
                    }),

                    Case("ApprovalAlwaysReturnsAlways", "Selecting Always returns the session-approve response", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Task<string> approval = app.RequestApprovalAsync(Tool("run_process"));
                            Feed(backend, app, Esc + "[B"); // Down -> 1
                            Feed(backend, app, Esc + "[B"); // Down -> 2 (Always)
                            Feed(backend, app, "\r");
                            MuxAssert.AreEqual("always", await approval.ConfigureAwait(false), "always");
                        }
                    }),

                    // ---- Approval modal (negative) ----
                    Case("ApprovalEscapeDenies", "Escaping the approval modal denies", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Task<string> approval = app.RequestApprovalAsync(Tool("write_file"));
                            backend.FeedInput(new byte[] { 0x1b }); // Escape
                            app.PumpInputOnce();
                            app.PumpInputOnce();
                            MuxAssert.AreEqual("n", await approval.ConfigureAwait(false), "escape -> deny");
                        }
                    }),

                    Case("ApprovalNullToolCallDoesNotCrash", "A null tool call still produces a usable modal", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            Task<string> approval = app.RequestApprovalAsync(null!);
                            MuxAssert.IsTrue(app.IsModalActive, "modal shown for null tool");
                            Feed(backend, app, "\r");
                            MuxAssert.AreEqual("y", await approval.ConfigureAwait(false), "approve");
                        }
                    }),

                    Case("ApprovalShutdownDenies", "Disposing the app while awaiting approval denies", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            Task<string> approval = app.RequestApprovalAsync(Tool("write_file"));
                            MuxAssert.IsTrue(app.IsModalActive, "modal shown");
                            app.Dispose(); // cancels the shell token -> modal closes with deny
                            MuxAssert.AreEqual("n", await approval.ConfigureAwait(false), "shutdown -> deny");
                        }
                    }),

                    // ---- Jobs modal ----
                    Case("JobsModalFocusesSelected", "Selecting a job in the jobs modal focuses it", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = NewAppNewJob(out HeadlessBackend backend, manager))
                        {
                            Feed(backend, app, "alpha" + "\r");
                            Feed(backend, app, "beta" + "\r");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            string firstId = app.JobIds[0];
                            MuxAssert.AreEqual(app.JobIds[1], app.FocusedJobId, "newest focused initially");

                            Feed(backend, app, "/jobs" + "\r"); // open jobs modal
                            MuxAssert.IsTrue(app.IsModalActive, "jobs modal open");
                            Feed(backend, app, "\r"); // Enter on option 0 (first job)

                            await WaitUntilAsync(() => app.FocusedJobId == firstId, ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(firstId, app.FocusedJobId, "focused the selected job");
                        }
                    }),

                    Case("JobsModalEmptyShowsMessage", "Opening the jobs modal with no jobs shows a message", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewAppNewJob(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/jobs" + "\r");
                            MuxAssert.IsTrue(app.IsModalActive, "message modal shown");
                            MuxAssert.AreEqual(0, app.JobIds.Count, "no jobs");

                            Feed(backend, app, "\r"); // dismiss OK
                            MuxAssert.IsFalse(app.IsModalActive, "message dismissed");
                        }
                    }),

                    Case("JobsModalEscapeKeepsFocus", "Escaping the jobs modal leaves focus unchanged", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = NewAppNewJob(out HeadlessBackend backend, manager))
                        {
                            Feed(backend, app, "alpha" + "\r");
                            Feed(backend, app, "beta" + "\r");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            string focusedBefore = app.FocusedJobId!;

                            Feed(backend, app, "/jobs" + "\r");
                            backend.FeedInput(new byte[] { 0x1b }); // Escape
                            app.PumpInputOnce();
                            app.PumpInputOnce();

                            MuxAssert.IsFalse(app.IsModalActive, "modal closed");
                            MuxAssert.AreEqual(focusedBefore, app.FocusedJobId, "focus unchanged");
                        }
                    }),

                    // ---- Quit confirmation + startup splash ----
                    Case("QuitOpensConfirmationModal", "Quitting opens a confirmation modal that Escape dismisses", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/quit" + "\r");
                            MuxAssert.IsTrue(app.IsModalActive, "quit confirmation shown");

                            backend.FeedInput(new byte[] { 0x1b }); // Escape -> cancel
                            app.PumpInputOnce();
                            app.PumpInputOnce();
                            MuxAssert.IsFalse(app.IsModalActive, "confirmation dismissed");
                        }
                    }),

                    Case("StartupSplashShownAndDismissed", "The startup splash modal shows and dismisses on Enter", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(100, 30);
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, null, string.Empty, string.Empty, null, showSplash: true))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsTrue(app.IsModalActive, "splash shown at startup");

                            app.Start();
                            app.RenderOnce();
                            MuxAssert.Contains("Joel Christner", backend.PeekOutput(), "splash shows copyright");

                            backend.FeedInput("\r");
                            app.PumpInputOnce();
                            MuxAssert.IsFalse(app.IsModalActive, "splash dismissed");
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static JobManager NewManager()
        {
            return new JobManager(EchoRunner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager)
        {
            backend = new HeadlessBackend(100, 24);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static MuxTuiApp NewAppNewJob(out HeadlessBackend backend, JobManager manager)
        {
            MuxTuiApp app = NewApp(out backend, manager);
            app.DefaultEnqueueBehavior = EnqueueBehavior.NewJob;
            return app;
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static ToolCall Tool(string name)
        {
            return new ToolCall { Id = "t1", Name = name, Arguments = "{}" };
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
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

        #endregion
    }
}
