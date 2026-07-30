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
    /// Touchstone suite for the shell sidebar and responsive collapse (<see cref="SidebarView"/> +
    /// <see cref="MuxTuiApp"/>). Uses a wide headless backend so the sidebar is visible, then drives job
    /// submission (through the real composer/input path), focus, state changes, manual collapse, and
    /// width-driven responsive collapse.
    /// </summary>
    public static class SidebarSuite
    {
        private const string SuiteId = "Sidebar";

        /// <summary>
        /// Builds the sidebar suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for sidebar cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Shell sidebar listing, focus marking, and responsive collapse",
                new List<TestCaseDescriptor>
                {
                    Case("SidebarEmptyInitially", "The sidebar shows a zero job count before any submission", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string sidebar = Join(app.SidebarSnapshot());
                            MuxAssert.Contains("JOBS (0)", sidebar, "zero jobs");
                            MuxAssert.Contains("no jobs", sidebar, "empty hint");
                        }
                    }),

                    Case("SidebarListsSubmittedJobs", "The sidebar lists each submitted job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Submit(backend, app, "alpha");
                            Submit(backend, app, "beta");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string sidebar = Join(app.SidebarSnapshot());
                            MuxAssert.Contains("JOBS (2)", sidebar, "two jobs");
                            MuxAssert.Contains("alpha", sidebar, "first job label");
                            MuxAssert.Contains("beta", sidebar, "second job label");
                        }
                    }),

                    Case("SidebarMarksFocusedJob", "The focus marker moves to the focused job", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 2))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Submit(backend, app, "alpha");
                            Submit(backend, app, "beta");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            // Newest (position 2) is focused after submit.
                            MuxAssert.Contains("▸2", Join(app.SidebarSnapshot()), "second focused");

                            MuxAssert.IsTrue(app.FocusByIndex(1), "focus first");
                            string sidebar = Join(app.SidebarSnapshot());
                            MuxAssert.Contains("▸1", sidebar, "first focused marker");
                            MuxAssert.IsFalse(sidebar.Contains("▸2", StringComparison.Ordinal), "second unmarked");
                        }
                    }),

                    Case("SidebarReflectsRunningThenCompleted", "The state glyph updates from running to completed", async (CancellationToken ct) =>
                    {
                        TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                        async IAsyncEnumerable<AgentEvent> Gated(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
                        {
                            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                            yield return CompletedEvent();
                        }

                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = new JobManager(Gated, maxConcurrency: 1))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Submit(backend, app, "work");
                            string jobId = app.JobIds[0];
                            await WaitForStateAsync(manager, jobId, JobState.Running, ct).ConfigureAwait(false);
                            app.FocusJob(jobId); // force a deterministic sidebar refresh
                            MuxAssert.Contains("↻", Join(app.SidebarSnapshot()), "running glyph");

                            release.TrySetResult(true);
                            await WaitForStateAsync(manager, jobId, JobState.Completed, ct).ConfigureAwait(false);
                            app.FocusJob(jobId);
                            MuxAssert.Contains("✓", Join(app.SidebarSnapshot()), "completed glyph");
                        }
                    }),

                    Case("ToggleCollapsesAndExpands", "Ctrl+B toggles the sidebar collapse state", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsFalse(app.IsSidebarCollapsed, "expanded at 120 cols");

                            app.ToggleSidebar();
                            MuxAssert.IsTrue(app.IsSidebarCollapsed, "collapsed after toggle");

                            app.ToggleSidebar();
                            MuxAssert.IsFalse(app.IsSidebarCollapsed, "expanded after second toggle");
                        }
                    }),

                    Case("ResponsiveAutoCollapseBelowThreshold", "Narrow widths auto-collapse and wide widths restore", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsFalse(app.IsSidebarCollapsed, "expanded at 120");

                            backend.Resize(80, 24);
                            app.ApplyResponsiveLayout();
                            MuxAssert.IsTrue(app.IsSidebarCollapsed, "collapsed at 80");

                            backend.Resize(120, 24);
                            app.ApplyResponsiveLayout();
                            MuxAssert.IsFalse(app.IsSidebarCollapsed, "expanded again at 120");
                        }
                    }),

                    Case("ManualCollapseOverridesResponsive", "A manual collapse persists even when wide enough", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            app.ToggleSidebar(); // manual collapse
                            MuxAssert.IsTrue(app.IsSidebarCollapsed, "manually collapsed");

                            app.ApplyResponsiveLayout(); // width 120 would otherwise expand
                            MuxAssert.IsTrue(app.IsSidebarCollapsed, "stays collapsed while manual");
                        }
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static HeadlessBackend Wide()
        {
            return new HeadlessBackend(120, 24);
        }

        private static JobManager NewManager(Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> runner)
        {
            return new JobManager(runner, maxConcurrency: 1);
        }

        private static MuxTuiApp NewApp(HeadlessBackend backend, JobManager manager)
        {
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(
            Job job,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return CompletedEvent();
        }

        private static RunCompletedEvent CompletedEvent()
        {
            return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            return string.Join("\n", lines);
        }

        private static async Task WaitForStateAsync(JobManager manager, string jobId, JobState expected, CancellationToken cancellationToken)
        {
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                while (!linked.Token.IsCancellationRequested)
                {
                    foreach (Job job in manager.Jobs)
                    {
                        if (string.Equals(job.Id, jobId, StringComparison.Ordinal) && job.State == expected)
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
