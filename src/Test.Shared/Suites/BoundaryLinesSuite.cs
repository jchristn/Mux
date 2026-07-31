namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Settings;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the optional dark-grey boundary lines: the <c>/borders</c> toggle persists the
    /// choice to <c>settings.json</c>, and an app started with boundaries on paints the horizontal and
    /// vertical rules into the frame. Cases isolate <c>settings.json</c> to a temp config directory via
    /// <c>MUX_CONFIG_DIR</c> so they never touch the real config.
    /// </summary>
    public static class BoundaryLinesSuite
    {
        private const string SuiteId = "BoundaryLines";

        /// <summary>
        /// Builds the boundary-lines suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for boundary-line cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Boundary-line toggle persistence and rendering",
                new List<TestCaseDescriptor>
                {
                    Case("ToggleBoundariesPersists", "The /borders toggle flips and persists showBoundaryLines", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend _, manager, boundaries: false))
                            {
                                MuxAssert.IsFalse(SettingsLoader.LoadSettings().ShowBoundaryLines, "off by default");

                                app.ToggleBoundaries();
                                MuxAssert.IsTrue(SettingsLoader.LoadSettings().ShowBoundaryLines, "persisted on");

                                app.ToggleBoundaries();
                                MuxAssert.IsFalse(SettingsLoader.LoadSettings().ShowBoundaryLines, "persisted off");
                            }
                        })),

                    Case("BoundariesRenderWhenOn", "An app with boundaries on paints the horizontal and vertical rules", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, boundaries: true))
                            {
                                await Task.CompletedTask.ConfigureAwait(false);
                                app.Start();
                                app.RenderOnce();

                                string frame = backend.PeekOutput();
                                MuxAssert.Contains("─", frame, "horizontal rule painted");
                                MuxAssert.Contains("│", frame, "vertical gutter rule painted");
                            }
                        })),

                    Case("BoundariesAbsentWhenOff", "An app with boundaries off paints no vertical gutter rule", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, boundaries: false))
                            {
                                await Task.CompletedTask.ConfigureAwait(false);
                                app.Start();
                                app.RenderOnce();

                                string frame = backend.PeekOutput();
                                MuxAssert.IsFalse(frame.Contains("│", StringComparison.Ordinal), "no vertical rule when off");
                            }
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task WithConfigDirAsync(Func<string, Task> body)
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-borders-" + Guid.NewGuid().ToString("N"));
            string? previous = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", dir);
            Directory.CreateDirectory(dir);
            try
            {
                await body(dir).ConfigureAwait(false);
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", previous);
                TryDelete(dir);
            }
        }

        private static JobManager NewManager()
        {
            return new JobManager(EchoRunner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager, bool boundaries)
        {
            backend = new HeadlessBackend(120, 30);
            return new MuxTuiApp(
                backend,
                manager,
                "demo",
                ApprovalPolicyEnum.AutoApprove,
                null,
                string.Empty,
                string.Empty,
                null,
                null,
                showSplash: false,
                showBoundaries: boundaries);
        }

        private static void TryDelete(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        #endregion
    }
}
