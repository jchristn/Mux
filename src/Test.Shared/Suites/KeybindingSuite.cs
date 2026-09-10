namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for custom keybindings: the <see cref="MuxCommandCatalog.ApplyOverrides"/> rebind /
    /// unbind semantics (with catalog order preserved and unknown ids ignored) and the
    /// <c>keybindings.json</c> load/save round trip. Parse-validation of chords happens in the shell against
    /// TUIKit and is exercised there; here the override logic and persistence are covered directly.
    /// </summary>
    public static class KeybindingSuite
    {
        private const string SuiteId = "Keybinding";

        /// <summary>
        /// Builds the keybinding suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the keybinding cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Custom keybinding overrides and persistence",
                new List<TestCaseDescriptor>
                {
                    Case("OverrideRebindsChord", "An override replaces a command's chord", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxCommandCatalog catalog = BuildCatalog();

                        catalog.ApplyOverrides(new Dictionary<string, string?> { { "mux.clear", "ctrl+k" } });

                        MuxAssert.AreEqual("ctrl+k", catalog.Find("mux.clear")?.Chord, "chord rebound");
                    }),

                    Case("OverrideUnbindsChord", "A null override unbinds a command's chord", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxCommandCatalog catalog = BuildCatalog();

                        catalog.ApplyOverrides(new Dictionary<string, string?> { { "mux.clear", null } });

                        MuxAssert.IsTrue(catalog.Find("mux.clear")?.Chord == null, "chord unbound");
                    }),

                    Case("OverrideIgnoresUnknownIds", "An override for an unknown command id is ignored", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxCommandCatalog catalog = BuildCatalog();
                        int before = catalog.Commands.Count;

                        catalog.ApplyOverrides(new Dictionary<string, string?> { { "mux.nonexistent", "ctrl+j" } });

                        MuxAssert.AreEqual(before, catalog.Commands.Count, "no command added");
                        MuxAssert.IsTrue(catalog.Find("mux.nonexistent") == null, "unknown id still absent");
                    }),

                    Case("OverridePreservesCatalogOrder", "Rebinding keeps a command in its original catalog position", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxCommandCatalog catalog = BuildCatalog();
                        int originalIndex = IndexOf(catalog, "mux.clear");

                        catalog.ApplyOverrides(new Dictionary<string, string?> { { "mux.clear", "ctrl+k" } });

                        MuxAssert.AreEqual(originalIndex, IndexOf(catalog, "mux.clear"), "position unchanged");
                    }),

                    Case("OverridePreservesHandlerAndAliases", "Rebinding preserves the handler and slash aliases", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        int hits = 0;
                        MuxCommandCatalog catalog = new MuxCommandCatalog();
                        catalog.Add(new CommandDescriptor("t.do", "Do", "ctrl+d", () => hits++, "T", new[] { "do", "run" }));

                        catalog.ApplyOverrides(new Dictionary<string, string?> { { "t.do", "ctrl+r" } });
                        CommandDescriptor? rebound = catalog.Find("t.do");
                        rebound!.Handler();

                        MuxAssert.AreEqual("ctrl+r", rebound.Chord, "new chord");
                        MuxAssert.AreEqual(1, hits, "same handler invoked");
                        MuxAssert.IsTrue(rebound.MatchesSlash("run"), "aliases preserved");
                    }),

                    SettingsCase("KeybindingsRoundTripThroughDisk", "keybindings.json saves and loads overrides", (string dir, CancellationToken ct) =>
                    {
                        Dictionary<string, string?> bindings = new Dictionary<string, string?>
                        {
                            { "mux.clear", "ctrl+k" },
                            { "mux.save", null }
                        };

                        SettingsLoader.SaveKeybindings(bindings);
                        Dictionary<string, string?> loaded = SettingsLoader.LoadKeybindings();

                        MuxAssert.AreEqual("ctrl+k", loaded["mux.clear"], "rebind persisted");
                        MuxAssert.IsTrue(loaded.ContainsKey("mux.save") && loaded["mux.save"] == null, "unbind persisted as null");
                        return Task.CompletedTask;
                    }),

                    SettingsCase("MissingKeybindingsReturnsEmpty", "A missing keybindings.json yields an empty override map", (string dir, CancellationToken ct) =>
                    {
                        Dictionary<string, string?> loaded = SettingsLoader.LoadKeybindings();
                        MuxAssert.AreEqual(0, loaded.Count, "empty when absent");
                        return Task.CompletedTask;
                    }),

                    SettingsCase("EnsureConfigDirectorySeedsKeybindings", "EnsureConfigDirectory seeds keybindings.json", (string dir, CancellationToken ct) =>
                    {
                        SettingsLoader.EnsureConfigDirectory();
                        MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "keybindings.json")), "seeded file exists");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static MuxCommandCatalog BuildCatalog()
        {
            MuxCommandCatalog catalog = new MuxCommandCatalog();
            catalog.Add(new CommandDescriptor("mux.quit", "Exit", "ctrl+q", () => { }, "Session", new[] { "quit" }));
            catalog.Add(new CommandDescriptor("mux.clear", "Clear transcript", "ctrl+l", () => { }, "View", new[] { "clear" }));
            catalog.Add(new CommandDescriptor("mux.save", "Save session", "ctrl+s", () => { }, "Session", new[] { "save" }));
            return catalog;
        }

        private static int IndexOf(MuxCommandCatalog catalog, string id)
        {
            for (int i = 0; i < catalog.Commands.Count; i++)
            {
                if (string.Equals(catalog.Commands[i].Id, id, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static TestCaseDescriptor SettingsCase(string id, string name, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_keybind_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempDir);
                try
                {
                    await body(tempDir, ct).ConfigureAwait(false);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                    try
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                    }
                    catch (IOException)
                    {
                    }
                }
            });
        }

        #endregion
    }
}
