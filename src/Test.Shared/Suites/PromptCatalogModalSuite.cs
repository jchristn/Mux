namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Touchstone.Core;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;

    /// <summary>
    /// Touchstone suite for the TUI <see cref="PromptCatalogModal"/>: it renders the operational-prompt
    /// catalog, edits a global-scoped entry (persisting an override), resets it, and refuses to override a
    /// profile-scoped persona entry. Each case runs against an isolated temporary config directory and
    /// invalidates the shared resolver so process-wide state never leaks between cases.
    /// </summary>
    public static class PromptCatalogModalSuite
    {
        private const string SuiteId = "PromptCatalogModal";

        /// <summary>
        /// Builds the prompt-catalog-modal suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "TUI operational-prompt catalog modal",
                new List<TestCaseDescriptor>
                {
                    Case("RendersCatalogWithoutError", "The modal renders the catalog list and detail pane", (string dir, CancellationToken ct) =>
                    {
                        PromptCatalogModal modal = new PromptCatalogModal();
                        CellBuffer buffer = new CellBuffer(140, 40);
                        modal.Render(new BufferSurface(buffer));
                        // Navigating and re-rendering must also be safe.
                        modal.HandleKey(KeyEvent.Special(KeyCode.Down));
                        modal.Render(new BufferSurface(buffer));
                        return Task.CompletedTask;
                    }),

                    Case("EditingGlobalEntryPersistsOverride", "Editing a global entry stores an override in prompts.json", (string dir, CancellationToken ct) =>
                    {
                        PromptCatalogModal modal = new PromptCatalogModal();
                        NavigateTo(modal, "compaction.user");
                        modal.HandleKey(KeyEvent.Char((int)'e'));   // begin edit (global -> editing)
                        modal.HandleKey(KeyEvent.Char((int)'X'));   // insert a character
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape)); // commit

                        Dictionary<string, string> overrides = SettingsLoader.LoadOperationalPrompts();
                        MuxAssert.IsTrue(overrides.ContainsKey("compaction.user"), "override persisted");
                        MuxAssert.Contains("X", overrides["compaction.user"], "edited text persisted");
                        return Task.CompletedTask;
                    }),

                    Case("ResetClearsOverride", "Resetting an overridden entry removes it", (string dir, CancellationToken ct) =>
                    {
                        PromptResolver.SetOverride("compaction.user", "Custom framing:");
                        PromptResolver.Invalidate();
                        MuxAssert.IsTrue(SettingsLoader.LoadOperationalPrompts().ContainsKey("compaction.user"), "precondition: overridden");

                        PromptCatalogModal modal = new PromptCatalogModal();
                        NavigateTo(modal, "compaction.user");
                        modal.HandleKey(KeyEvent.Char((int)'r')); // reset

                        MuxAssert.IsFalse(SettingsLoader.LoadOperationalPrompts().ContainsKey("compaction.user"), "override cleared");
                        return Task.CompletedTask;
                    }),

                    Case("ProfileScopedEntryCannotBeOverridden", "A persona (profile-scoped) entry is read-only in the catalog modal", (string dir, CancellationToken ct) =>
                    {
                        PromptCatalogModal modal = new PromptCatalogModal();
                        NavigateTo(modal, "system");
                        modal.HandleKey(KeyEvent.Char((int)'e'));   // begin edit is refused for profile scope
                        modal.HandleKey(KeyEvent.Char((int)'Z'));   // would-be edit keystroke
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        MuxAssert.IsFalse(SettingsLoader.LoadOperationalPrompts().ContainsKey("system"), "no override created for a persona prompt");
                        return Task.CompletedTask;
                    })
                });
        }

        private static void NavigateTo(PromptCatalogModal modal, string key)
        {
            int target = SortedIndexOf(key);
            for (int i = 0; i < target; i++)
            {
                modal.HandleKey(KeyEvent.Special(KeyCode.Down));
            }
        }

        private static int SortedIndexOf(string key)
        {
            List<PromptDefinition> list = new List<PromptDefinition>(PromptCatalog.All);
            list.Sort((a, b) =>
            {
                int byKind = ((int)a.Kind).CompareTo((int)b.Kind);
                return byKind != 0 ? byKind : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Key, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private static TestCaseDescriptor Case(string id, string name, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_pcm_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                string? original = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
                Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", tempDir);
                PromptResolver.Invalidate();
                try
                {
                    await body(tempDir, ct).ConfigureAwait(false);
                }
                finally
                {
                    Environment.SetEnvironmentVariable("MUX_CONFIG_DIR", original);
                    PromptResolver.Invalidate();
                    try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch (IOException) { }
                }
            });
        }
    }
}
