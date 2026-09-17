namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Desktop.Prompting;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the desktop <see cref="PromptCatalogViewModel"/> logic layer (Mux.Desktop.Core):
    /// row projection grouped by kind with correct override flags, edit persistence, reset, and the
    /// profile-scoped / placeholder-validation guards. Avalonia rendering is not exercised. Each case runs
    /// against an isolated config directory and invalidates the shared resolver.
    /// </summary>
    public static class DesktopPromptCatalogSuite
    {
        /// <summary>
        /// Builds the desktop prompt-catalog suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("RowsSortedByKindWithFlags", "Rows project every catalog entry sorted by kind then name, with editable/override flags", (string dir, CancellationToken ct) =>
                {
                    PromptCatalogViewModel vm = new PromptCatalogViewModel();
                    MuxAssert.IsTrue(vm.Rows.Count == PromptCatalog.All.Count, "row per catalog entry");
                    for (int i = 1; i < vm.Rows.Count; i++)
                    {
                        int byKind = string.Compare(vm.Rows[i - 1].Kind, vm.Rows[i].Kind, StringComparison.Ordinal);
                        MuxAssert.IsTrue(byKind <= 0, "kind order at " + i);
                    }
                    bool sawTool = false, sawSystem = false;
                    foreach (PromptCatalogRow row in vm.Rows)
                    {
                        if (row.Key == "tool.read_file") { sawTool = true; MuxAssert.IsTrue(row.Editable, "tool row editable"); }
                        if (row.Key == "system") { sawSystem = true; MuxAssert.IsFalse(row.Editable, "system row not editable"); }
                    }
                    MuxAssert.IsTrue(sawTool, "tool row present");
                    MuxAssert.IsTrue(sawSystem, "system row present");
                    return Task.CompletedTask;
                }),

                Case("EditPersistsOverride", "TrySetOverride stores an override and the reloaded row reflects it", (string dir, CancellationToken ct) =>
                {
                    PromptCatalogViewModel vm = new PromptCatalogViewModel();
                    bool ok = vm.TrySetOverride("title.system", "Custom title rule.", out string error);
                    MuxAssert.IsTrue(ok, "set ok: " + error);
                    MuxAssert.IsTrue(SettingsLoader.LoadOperationalPrompts().ContainsKey("title.system"), "persisted");
                    PromptCatalogRow row = FindRow(vm, "title.system");
                    MuxAssert.IsTrue(row.Overridden, "row overridden");
                    MuxAssert.AreEqual("Custom title rule.", row.Effective, "row effective");
                    return Task.CompletedTask;
                }),

                Case("ResetClearsOverride", "Reset clears the override and restores the default", (string dir, CancellationToken ct) =>
                {
                    PromptCatalogViewModel vm = new PromptCatalogViewModel();
                    vm.TrySetOverride("title.system", "Custom.", out _);
                    vm.Reset("title.system");
                    MuxAssert.IsFalse(SettingsLoader.LoadOperationalPrompts().ContainsKey("title.system"), "override removed");
                    PromptCatalogRow row = FindRow(vm, "title.system");
                    MuxAssert.IsFalse(row.Overridden, "not overridden");
                    MuxAssert.AreEqual(PromptCatalog.DefaultFor("title.system"), row.Effective, "default restored");
                    return Task.CompletedTask;
                }),

                Case("ProfileScopedRejected", "A profile-scoped key cannot be overridden through the view-model", (string dir, CancellationToken ct) =>
                {
                    PromptCatalogViewModel vm = new PromptCatalogViewModel();
                    bool ok = vm.TrySetOverride("system", "x", out string error);
                    MuxAssert.IsFalse(ok, "rejected");
                    MuxAssert.IsTrue(error.Length > 0, "error message present");
                    return Task.CompletedTask;
                }),

                Case("MissingPlaceholderRejected", "An override dropping a required placeholder is rejected with a message", (string dir, CancellationToken ct) =>
                {
                    PromptCatalogViewModel vm = new PromptCatalogViewModel();
                    bool ok = vm.TrySetOverride("tool.run_process", "no tokens here", out string error);
                    MuxAssert.IsFalse(ok, "rejected");
                    MuxAssert.IsTrue(error.Length > 0, "error message present");
                    return Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor("DesktopPromptCatalog", "Desktop prompt-catalog view-model projection, edit, and reset", cases);
        }

        private static PromptCatalogRow FindRow(PromptCatalogViewModel vm, string key)
        {
            foreach (PromptCatalogRow row in vm.Rows)
            {
                if (row.Key == key) return row;
            }

            throw new Exception("Row not found: " + key);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("DesktopPromptCatalog", caseId, displayName, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_dcat_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string? originalConfigDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempDir);
                PromptResolver.Invalidate();
                try
                {
                    await body(tempDir, ct).ConfigureAwait(false);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", originalConfigDir);
                    PromptResolver.Invalidate();
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (IOException) { }
                }
            });
        }
    }
}
