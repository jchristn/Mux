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
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the global settings editor: the <c>/settings</c> slash surface, the modal it
    /// opens, and the edit → persist (<c>settings.json</c>) → live-apply (settings-changed callback) round
    /// trip. Each case isolates <c>settings.json</c> to a temp config directory via the AsyncLocal config
    /// override so a fire-and-forget modal-resolution continuation writes into this test's own directory.
    /// </summary>
    public static class SettingsSuite
    {
        private const string SuiteId = "Settings";
        private const string Esc = "\x1b";

        /// <summary>
        /// Builds the settings suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for settings cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Global settings editor: slash surface, modal, and persist/apply round trip",
                new List<TestCaseDescriptor>
                {
                    Case("SlashResolvesSettings", "The slash parser resolves the settings command and its aliases", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out _, manager, null))
                            {
                                SlashCommandParser parser = new SlashCommandParser(app.Catalog);
                                MuxAssert.AreEqual("mux.settings", parser.Resolve("/settings")?.Id, "settings alias");
                                MuxAssert.AreEqual("mux.settings", parser.Resolve("/config")?.Id, "config alias");
                                MuxAssert.AreEqual("mux.settings", parser.Resolve("/preferences")?.Id, "preferences alias");
                                MuxAssert.AreEqual("mux.settings", parser.Resolve("/prefs")?.Id, "prefs alias");
                            }
                        })),

                    Case("SettingsCommandOpensModal", "The /settings command opens the settings editor modal", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, null))
                            {
                                Feed(backend, app, "/settings" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);
                                MuxAssert.IsTrue(app.IsModalActive, "settings modal open");

                                // The modal renders its fields through the full pipeline.
                                app.Start();
                                app.RenderOnce();
                                MuxAssert.Contains("Max agent iterations", backend.PeekOutput(), "modal lists the iteration cap field");
                            }
                        })),

                    Case("SettingsCancelLeavesDiskUntouched", "Cancelling the modal with Esc does not write settings.json", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            string settingsPath = Path.Combine(dir, "settings.json");
                            MuxAssert.IsFalse(File.Exists(settingsPath), "no settings file before");

                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, null))
                            {
                                Feed(backend, app, "/settings" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);

                                // A lone Escape byte is held pending until a subsequent no-input pump flushes
                                // it (there is no trailing byte to disambiguate it from an escape sequence),
                                // so feed it and then pump once more with no input to deliver the key.
                                Feed(backend, app, Esc);
                                Pump(app);
                                await WaitUntilAsync(() => !app.IsModalActive, ct).ConfigureAwait(false);

                                MuxAssert.IsFalse(File.Exists(settingsPath), "cancel wrote nothing");
                            }
                        })),

                    Case("SettingsEditFiresApplyHook", "Editing a field builds the value into the settings and fires the apply hook", (CancellationToken ct) =>
                        WithConfigDirAsync(async dir =>
                        {
                            MuxSettings? applied = null;
                            await using (JobManager manager = NewManager())
                            using (MuxTuiApp app = NewApp(out HeadlessBackend backend, manager, s => applied = s))
                            {
                                Feed(backend, app, "/settings" + "\r");
                                await WaitModal(app, ct).ConfigureAwait(false);

                                // Field 0 (Max agent iterations) is focused. Tab once to the Max token budget
                                // field, which starts blank (the default is off), type a value, then save.
                                Feed(backend, app, "\t");
                                Feed(backend, app, "12345");
                                Feed(backend, app, "\r");

                                // The modal parses the edited field into the settings instance, saves it,
                                // and fires the settings-changed apply hook on a continuation; poll for the
                                // hook so the assertion does not race the fire-and-forget resolution. (The
                                // settings.json disk round trip itself is covered by the SettingsLoader suite.)
                                await WaitUntilAsync(() => applied != null, ct).ConfigureAwait(false);
                                MuxAssert.AreEqual(12345, applied!.MaxTokenBudget ?? -1, "apply hook carried the edited budget");
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
            string dir = Path.Combine(Path.GetTempPath(), "mux-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            using (SettingsLoader.PushConfigDirectoryOverride(dir))
            {
                try
                {
                    await body(dir).ConfigureAwait(false);
                }
                finally
                {
                    TryDelete(dir);
                }
            }
        }

        private static JobManager NewManager()
        {
            return new JobManager(EchoRunner, maxConcurrency: 2);
        }

        private static MuxTuiApp NewApp(out HeadlessBackend backend, JobManager manager, Action<MuxSettings>? onSettingsChanged)
        {
            backend = new HeadlessBackend(100, 30);
            return new MuxTuiApp(
                backend,
                manager,
                "demo",
                ApprovalPolicyEnum.AutoApprove,
                onSettingsChanged: onSettingsChanged);
        }

        private static void Feed(HeadlessBackend backend, MuxTuiApp app, string input)
        {
            backend.FeedInput(input);
            app.PumpInputOnce();
        }

        private static void Pump(MuxTuiApp app)
        {
            // A no-input pump; flushes a pending lone Escape byte that has no trailing disambiguation byte.
            app.PumpInputOnce();
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

        private static Task WaitModal(MuxTuiApp app, CancellationToken ct)
        {
            return WaitUntilAsync(() => app.IsModalActive, ct);
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
