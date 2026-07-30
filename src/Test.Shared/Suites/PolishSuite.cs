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
    /// Touchstone suite for M14 polish: theme cycling, mouse-capture toggle, and resize repaint.
    /// Positive, command-path, determinism, and idempotence cases.
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
                "Theme, mouse, and resize polish",
                new List<TestCaseDescriptor>
                {
                    Case("ThemeCyclesAndWraps", "Cycling the theme advances and wraps back", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string start = app.ThemeName; // "mux" default
                            app.CycleTheme();
                            MuxAssert.IsFalse(string.Equals(start, app.ThemeName, StringComparison.Ordinal), "theme changed");
                            app.CycleTheme();
                            app.CycleTheme();
                            app.CycleTheme(); // four presets (mux + three built-ins) -> back to start
                            MuxAssert.AreEqual(start, app.ThemeName, "wrapped to start");
                        }
                    }),

                    Case("ThemeSelectorOpensViaSlash", "The /theme command opens the theme selector", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            Feed(backend, app, "/theme" + "\r");
                            MuxAssert.IsTrue(app.IsModalActive, "theme selector modal open");
                        }
                    }),

                    Case("ApplyThemeConformsAndWraps", "Applying a theme by index switches and wraps", async (CancellationToken ct) =>
                    {
                        await using (JobManager manager = NewManager())
                        using (MuxTuiApp app = NewApp(out _, manager))
                        {
                            await Task.CompletedTask.ConfigureAwait(false);
                            string start = app.ThemeName; // "mux" default at index 0
                            app.ApplyTheme(1);
                            MuxAssert.IsFalse(string.Equals(start, app.ThemeName, StringComparison.Ordinal), "theme changed");
                            MuxAssert.Contains("mux", app.RenderRegion("transcript", 80, 10), "renders under new theme");
                            app.ApplyTheme(0);
                            MuxAssert.AreEqual(start, app.ThemeName, "back to default at index 0");
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
