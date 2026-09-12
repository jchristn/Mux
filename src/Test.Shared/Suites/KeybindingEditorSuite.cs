namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.Services;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the desktop keybindings editor: <see cref="KeybindingCatalog"/> content and the
    /// <see cref="KeybindingEditorModel"/> rebind / unbind / reset semantics and effective-chord resolution,
    /// including the "override that matches the default is dropped" and "unknown ids are ignored" rules.
    /// </summary>
    public static class KeybindingEditorSuite
    {
        private const string SuiteId = "KeybindingEditor";

        /// <summary>
        /// Builds the keybinding-editor suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the keybinding-editor cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Desktop keybindings editor model",
                new List<TestCaseDescriptor>
                {
                    Case("CatalogExposesKnownCommands", "The catalog contains the core commands with defaults", ct =>
                    {
                        MuxAssert.IsTrue(KeybindingCatalog.Commands.Count > 10, "catalog is populated");
                        MuxAssert.AreEqual("ctrl+l", KeybindingCatalog.Find("mux.clear")?.DefaultChord, "clear default chord");
                        MuxAssert.IsTrue(KeybindingCatalog.Find("mux.export")?.DefaultChord == null, "export ships unbound");
                        MuxAssert.IsTrue(KeybindingCatalog.Find("mux.nope") == null, "unknown id absent");
                        return Task.CompletedTask;
                    }),

                    Case("EffectiveFallsBackToDefault", "With no overrides the effective chord is the default", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(null);
                        MuxAssert.AreEqual("ctrl+l", model.EffectiveChord("mux.clear"), "default chord");
                        MuxAssert.IsFalse(model.IsOverridden("mux.clear"), "not overridden");
                        return Task.CompletedTask;
                    }),

                    Case("RebindRecordsOverride", "Rebinding to a new chord records an override", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(null);
                        model.SetChord("mux.clear", "Ctrl+K");
                        MuxAssert.AreEqual("ctrl+k", model.EffectiveChord("mux.clear"), "override normalized + applied");
                        MuxAssert.IsTrue(model.IsOverridden("mux.clear"), "now overridden");
                        MuxAssert.IsTrue(model.Overrides.ContainsKey("mux.clear"), "persisted entry present");
                        return Task.CompletedTask;
                    }),

                    Case("RebindToDefaultDropsOverride", "Rebinding to the shipped default drops the override", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(new Dictionary<string, string?> { { "mux.clear", "ctrl+k" } });
                        MuxAssert.IsTrue(model.IsOverridden("mux.clear"), "starts overridden");
                        model.SetChord("mux.clear", "ctrl+l");
                        MuxAssert.IsFalse(model.IsOverridden("mux.clear"), "override dropped");
                        MuxAssert.AreEqual("ctrl+l", model.EffectiveChord("mux.clear"), "back to default");
                        return Task.CompletedTask;
                    }),

                    Case("UnbindRecordsNull", "Unbinding a defaulted command records an explicit null", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(null);
                        model.Unbind("mux.clear");
                        MuxAssert.IsTrue(model.EffectiveChord("mux.clear") == null, "now unbound");
                        MuxAssert.IsTrue(model.Overrides.ContainsKey("mux.clear") && model.Overrides["mux.clear"] == null, "null override persisted");
                        return Task.CompletedTask;
                    }),

                    Case("UnbindAlreadyDefaultedIsClean", "Unbinding a command whose default is null records nothing", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(null);
                        model.Unbind("mux.export");
                        MuxAssert.IsFalse(model.Overrides.ContainsKey("mux.export"), "no needless override");
                        MuxAssert.IsTrue(model.EffectiveChord("mux.export") == null, "still unbound");
                        return Task.CompletedTask;
                    }),

                    Case("ResetRemovesOverride", "Reset restores the shipped default", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(new Dictionary<string, string?> { { "mux.clear", null } });
                        model.Reset("mux.clear");
                        MuxAssert.IsFalse(model.IsOverridden("mux.clear"), "override cleared");
                        MuxAssert.AreEqual("ctrl+l", model.EffectiveChord("mux.clear"), "default restored");
                        return Task.CompletedTask;
                    }),

                    Case("FindConflictDetectsSharedChord", "A chord already bound to another command is reported", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(null);
                        // mux.clear defaults to ctrl+l; rebinding mux.usage to ctrl+l should conflict with it.
                        MuxAssert.AreEqual("mux.clear", model.FindConflict("mux.usage", "Ctrl+L"), "detects the default holder");
                        MuxAssert.IsTrue(model.FindConflict("mux.clear", "ctrl+l") == null, "same command is not its own conflict");
                        MuxAssert.IsTrue(model.FindConflict("mux.usage", "ctrl+shift+f9") == null, "a free chord has no conflict");
                        MuxAssert.IsTrue(model.FindConflict("mux.usage", null) == null, "unbinding never conflicts");
                        return Task.CompletedTask;
                    }),

                    Case("UnknownOverridesAndSetsIgnored", "Unknown command ids are dropped on load and ignored on set", ct =>
                    {
                        KeybindingEditorModel model = new KeybindingEditorModel(new Dictionary<string, string?> { { "mux.ghost", "ctrl+j" } });
                        MuxAssert.IsFalse(model.Overrides.ContainsKey("mux.ghost"), "unknown load entry dropped");
                        model.SetChord("mux.ghost", "ctrl+j");
                        MuxAssert.IsFalse(model.Overrides.ContainsKey("mux.ghost"), "unknown set ignored");
                        return Task.CompletedTask;
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
