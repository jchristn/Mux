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
                    Case("SidebarShowsTelemetryHeaders", "The sidebar shows status and telemetry sections before any turn", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string sidebar = Join(app.SidebarSnapshot());
                            MuxAssert.Contains("STATUS", sidebar, "status header");
                            MuxAssert.Contains("idle", sidebar, "idle before any turn");
                            MuxAssert.Contains("THIS TURN", sidebar, "this-turn header");
                            MuxAssert.Contains("SESSION", sidebar, "session header");
                            MuxAssert.Contains("TTFT", sidebar, "ttft row");
                        }
                    }),

                    Case("SidebarShowsModel", "The sidebar header shows the active model, not the endpoint name", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, endpointName: "local-llm", model: "qwen2.5-coder"))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string sidebar = Join(app.SidebarSnapshot());
                            MuxAssert.Contains("qwen2.5-coder", sidebar, "model shown in the header");
                            MuxAssert.IsFalse(sidebar.Contains("local-llm", StringComparison.Ordinal), "endpoint name not shown");
                        }
                    }),

                    Case("SidebarCountsCompletedTurns", "The session turn count increments after a turn completes", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = Wide();
                        await using (JobManager manager = NewManager(EchoRunner))
                        using (MuxTuiApp app = NewApp(backend, manager))
                        {
                            Submit(backend, app, "alpha");
                            await app.DrainProjectorsAsync().ConfigureAwait(false);

                            string sidebar = Join(app.SidebarSnapshot());
                            MuxAssert.Contains("idle", sidebar, "idle after turn");
                            MuxAssert.Contains("Turns", sidebar, "turns row present");
                            MuxAssert.AreEqual(1, app.TurnCount, "one turn ran");
                        }
                    }),

                    Case("SidebarReflectsBusyThenIdle", "Status shows running while a turn is active and idle after", async (CancellationToken ct) =>
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
                            await WaitForStateAsync(manager, app.JobIds[0], JobState.Running, ct).ConfigureAwait(false);
                            MuxAssert.Contains("running", Join(app.SidebarSnapshot()), "running status");

                            release.TrySetResult(true);
                            await app.DrainProjectorsAsync().ConfigureAwait(false);
                            MuxAssert.Contains("idle", Join(app.SidebarSnapshot()), "idle status");
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
            MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
            // These cases exercise the sidebar, not the submit chooser; make submit deterministic.
            return app;
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
