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
    /// Touchstone suite for M14 polish: theme cycling, density toggle, mouse-capture toggle, and resize
    /// repaint. Positive, command-path, determinism, and idempotence cases.
    /// </summary>
    public static class PolishSuite
    {
        private const string SuiteId = "Polish";
        private const char Esc = (char)27;

        /// <summary>
        /// Builds the polish suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for polish cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Theme, density, mouse, and resize polish",
                new List<TestCaseDescriptor>
                {
                    Case("ThemeCyclesAndWraps", "Cycling the theme advances and wraps back", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string start = app.ThemeName;
                            app.CycleTheme();
                            MuxAssert.IsFalse(string.Equals(start, app.ThemeName, StringComparison.Ordinal), "theme changed");
                            app.CycleTheme();
                            app.CycleTheme(); // three presets -> back to start
                            MuxAssert.AreEqual(start, app.ThemeName, "wrapped to start");
                        }
                    }),

                    Case("ThemeCycleViaSlash", "The /theme command changes the theme", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string start = app.ThemeName;
                            Feed(backend, app, "/theme" + "\r");
                            MuxAssert.IsFalse(string.Equals(start, app.ThemeName, StringComparison.Ordinal), "theme changed via slash");
                        }
                    }),

                    Case("RenderStableAfterThemeSwitch", "Rendering still works after switching theme", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            app.CycleTheme();
                            string frame = app.RenderRegion("transcript", 80, 10);
                            MuxAssert.Contains("mux", frame, "transcript still renders");
                            MuxAssert.AreEqual(frame, app.RenderRegion("transcript", 80, 10), "deterministic after theme switch");
                        }
                    }),

                    Case("DensityTogglesAndRestores", "Toggling density flips and restores compact state", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsFalse(app.IsCompact, "comfortable by default");
                            app.ToggleDensity();
                            MuxAssert.IsTrue(app.IsCompact, "compact after toggle");
                            MuxAssert.Contains("mux", app.RenderRegion("transcript", 80, 10), "renders while compact");
                            app.ToggleDensity();
                            MuxAssert.IsFalse(app.IsCompact, "comfortable after second toggle");
                        }
                    }),

                    Case("DensityViaSlash", "The /density command toggles compact", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/density" + "\r");
                            MuxAssert.IsTrue(app.IsCompact, "compact via slash");
                        }
                    }),

                    Case("MouseCaptureToggles", "Toggling mouse capture flips the flag", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            bool start = app.IsMouseCaptureEnabled;
                            app.ToggleMouseCapture();
                            MuxAssert.IsFalse(start == app.IsMouseCaptureEnabled, "capture flipped");
                            app.ToggleMouseCapture();
                            MuxAssert.AreEqual(start, app.IsMouseCaptureEnabled, "restored after second toggle");
                        }
                    }),

                    Case("MouseCaptureTogglesViaF12", "F12 toggles mouse capture", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            bool start = app.IsMouseCaptureEnabled;
                            Feed(backend, app, Esc + "[24~"); // F12
                            MuxAssert.IsFalse(start == app.IsMouseCaptureEnabled, "F12 flipped capture");
                        }
                    }),

                    // ---- Resize repaint ----
                    Case("RendersAtMultipleSizes", "The transcript renders at different widths", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.Contains("mux", app.RenderRegion("transcript", 120, 12), "wide render");
                            MuxAssert.Contains("mux", app.RenderRegion("transcript", 40, 8), "narrow render");
                        }
                    }),

                    Case("ResizeDrivesResponsiveCollapse", "Resizing across the breakpoint repaints the collapse state", async (CancellationToken ct) =>
                    {
                        HeadlessBackend backend = new HeadlessBackend(120, 30);
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            MuxAssert.IsFalse(app.IsSidebarCollapsed, "expanded at 120");

                            backend.Resize(70, 30);
                            app.ApplyResponsiveLayout();
                            MuxAssert.IsTrue(app.IsSidebarCollapsed, "collapsed at 70");
                            MuxAssert.Contains("mux", app.RenderRegion("transcript", 70, 12), "renders after collapse");

                            backend.Resize(140, 30);
                            app.ApplyResponsiveLayout();
                            MuxAssert.IsFalse(app.IsSidebarCollapsed, "expanded again at 140");
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
            backend = new HeadlessBackend(120, 30);
            return new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove);
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        #endregion
    }
}
