namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the prompt catalog foundation: catalog completeness, the operational override
    /// round-trip through <c>prompts.json</c>, reset semantics, resolver precedence and substitution,
    /// placeholder validation, and the consolidation of the two formerly-duplicated compaction literals.
    /// Each case runs against an isolated temporary config directory (via <c>MUX_CONFIG_DIR</c>) and
    /// invalidates the shared resolver so the process-wide cache never leaks across cases.
    /// </summary>
    public static class PromptCatalogSuite
    {
        /// <summary>
        /// Builds the prompt-catalog suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the prompt-catalog cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                Case("EveryEntryHasKeyDisplayAndDefault", "Every catalog entry has a unique key, a display name, and non-empty default content", (string dir, CancellationToken ct) =>
                {
                    HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                    foreach (PromptDefinition definition in PromptCatalog.All)
                    {
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(definition.Key), "key non-empty");
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(definition.DisplayName), "display non-empty: " + definition.Key);
                        MuxAssert.IsFalse(string.IsNullOrEmpty(definition.DefaultContent), "default non-empty: " + definition.Key);
                        MuxAssert.IsTrue(keys.Add(definition.Key), "unique key: " + definition.Key);
                    }
                    return Task.CompletedTask;
                }),

                Case("EveryOperationalKindIsRepresented", "Every kind except the external subagent personas has at least one catalog entry", (string dir, CancellationToken ct) =>
                {
                    PromptKind[] expected = new[]
                    {
                        PromptKind.SystemPersona, PromptKind.Compaction, PromptKind.TaskPlanning, PromptKind.TitleGeneration,
                        PromptKind.ToolSection, PromptKind.ToolDescription, PromptKind.ToolResult, PromptKind.Diagnostics
                    };
                    foreach (PromptKind kind in expected)
                    {
                        bool found = false;
                        foreach (PromptDefinition definition in PromptCatalog.All)
                        {
                            if (definition.Kind == kind) { found = true; break; }
                        }
                        MuxAssert.IsTrue(found, "kind represented: " + kind);
                    }
                    return Task.CompletedTask;
                }),

                Case("DefaultForKnownSourcesFromDefaultsAndUnknownIsEmpty", "DefaultFor returns the coded default for a known key and empty for an unknown key", (string dir, CancellationToken ct) =>
                {
                    MuxAssert.AreEqual(Defaults.SystemPrompt, PromptCatalog.DefaultFor("system"), "system default sources from Defaults");
                    MuxAssert.AreEqual(Defaults.CompactionSystemPrompt, PromptCatalog.DefaultFor("compaction.system"), "compaction.system sources from Defaults");
                    MuxAssert.AreEqual(string.Empty, PromptCatalog.DefaultFor("does.not.exist"), "unknown key empty");
                    return Task.CompletedTask;
                }),

                Case("OverrideRoundTripsThroughSaveAndLoad", "An operational override persists to prompts.json and resolves back", (string dir, CancellationToken ct) =>
                {
                    PromptResolver.SetOverride("title.system", "Custom title instruction.");
                    Dictionary<string, string> loaded = SettingsLoader.LoadOperationalPrompts();
                    MuxAssert.IsTrue(loaded.ContainsKey("title.system"), "persisted");
                    MuxAssert.AreEqual("Custom title instruction.", loaded["title.system"], "persisted value");
                    MuxAssert.AreEqual("Custom title instruction.", PromptResolver.Shared.GetEffective("title.system"), "shared resolves override");
                    MuxAssert.IsTrue(PromptResolver.Shared.IsOverridden("title.system"), "reported overridden");
                    return Task.CompletedTask;
                }),

                Case("ResetRestoresDefault", "Resetting a key clears the override and restores the catalog default", (string dir, CancellationToken ct) =>
                {
                    PromptResolver.SetOverride("title.system", "Custom.");
                    PromptResolver.ResetToDefault("title.system");
                    MuxAssert.IsFalse(SettingsLoader.LoadOperationalPrompts().ContainsKey("title.system"), "override removed");
                    MuxAssert.AreEqual(PromptCatalog.DefaultFor("title.system"), PromptResolver.Shared.GetEffective("title.system"), "default restored");
                    MuxAssert.IsFalse(PromptResolver.Shared.IsOverridden("title.system"), "not overridden");
                    return Task.CompletedTask;
                }),

                Case("ResolverReturnsOverrideElseDefault", "A resolver returns the override when set and the default otherwise", (string dir, CancellationToken ct) =>
                {
                    PromptResolver withOverride = new PromptResolver(new Dictionary<string, string> { { "probe.system", "OVERRIDE" } });
                    MuxAssert.AreEqual("OVERRIDE", withOverride.GetEffective("probe.system"), "override wins");
                    MuxAssert.AreEqual(PromptCatalog.DefaultFor("probe.user"), withOverride.GetEffective("probe.user"), "default for unset");

                    PromptResolver empty = new PromptResolver(null);
                    MuxAssert.AreEqual(PromptCatalog.DefaultFor("probe.system"), empty.GetEffective("probe.system"), "empty falls back");
                    return Task.CompletedTask;
                }),

                Case("DuplicatedCompactionLiteralsConsolidated", "The synthetic-summary marker and compaction framing resolve to a single catalog entry", (string dir, CancellationToken ct) =>
                {
                    MuxAssert.AreEqual(PromptCatalog.SyntheticSummaryPrefix, ConversationCompactor.SummaryPrefix, "compactor prefix == catalog marker");
                    MuxAssert.AreEqual(PromptCatalog.SyntheticSummaryPrefix, PromptCatalog.DefaultFor("compaction.summary-prefix"), "catalog default == marker");
                    MuxAssert.AreEqual("Compact this older conversation history:", PromptCatalog.DefaultFor("compaction.user"), "compaction framing is the single catalog default");
                    return Task.CompletedTask;
                }),

                Case("ValidationRejectsMissingPlaceholder", "An override that drops a required placeholder is rejected", (string dir, CancellationToken ct) =>
                {
                    bool valid = PromptResolver.TryValidateOverride("tool.run_process", "No tokens here.", out IReadOnlyList<string> missing);
                    MuxAssert.IsFalse(valid, "invalid");
                    MuxAssert.IsTrue(missing.Count >= 1, "missing reported");
                    MuxAssert.Throws<ArgumentException>(() => PromptResolver.SetOverride("tool.run_process", "No tokens."), "set throws");
                    return Task.CompletedTask;
                }),

                Case("ValidationAcceptsPreservedPlaceholders", "An override that preserves every required placeholder is accepted and persists", (string dir, CancellationToken ct) =>
                {
                    string content = "Shell tool on {OperatingSystem} via {Shell} {ShellArgsHint}.";
                    MuxAssert.IsTrue(PromptResolver.TryValidateOverride("tool.run_process", content, out _), "valid");
                    PromptResolver.SetOverride("tool.run_process", content);
                    string resolved = PromptResolver.Shared.Resolve("tool.run_process", new Dictionary<string, string>
                    {
                        { "{OperatingSystem}", "TestOS" }, { "{Shell}", "sh" }, { "{ShellArgsHint}", "-c" }
                    });
                    MuxAssert.AreEqual("Shell tool on TestOS via sh -c.", resolved, "substituted");
                    return Task.CompletedTask;
                }),

                Case("SetOverrideRejectsProfileScopedKey", "A profile-scoped key cannot be set as an operational override", (string dir, CancellationToken ct) =>
                {
                    MuxAssert.Throws<ArgumentException>(() => PromptResolver.SetOverride("system", "x"), "profile scope rejected");
                    return Task.CompletedTask;
                }),

                Case("SetOverrideRejectsUnknownKey", "An unknown key cannot be set as an operational override", (string dir, CancellationToken ct) =>
                {
                    MuxAssert.Throws<ArgumentException>(() => PromptResolver.SetOverride("no.such.key", "x"), "unknown key rejected");
                    return Task.CompletedTask;
                }),

                Case("ResolveSubstitutesPlaceholders", "Resolve substitutes runtime placeholders in the effective text", (string dir, CancellationToken ct) =>
                {
                    PromptResolver resolver = new PromptResolver(null);
                    string resolved = resolver.Resolve("result.unknown_tool", new Dictionary<string, string> { { "{ToolName}", "frobnicate" } });
                    MuxAssert.Contains("frobnicate", resolved, "name substituted");
                    MuxAssert.IsFalse(resolved.Contains("{ToolName}"), "token consumed");
                    return Task.CompletedTask;
                }),

                Case("OperationalOverridesAndProfilesCoexist", "Saving operational overrides preserves profiles and vice versa", (string dir, CancellationToken ct) =>
                {
                    SettingsLoader.SavePrompts(new List<Mux.Core.Models.PromptProfile>
                    {
                        new Mux.Core.Models.PromptProfile { Name = "Custom", IsActive = true, SystemPrompt = "persona" }
                    });
                    PromptResolver.SetOverride("title.system", "op-override");

                    List<Mux.Core.Models.PromptProfile> profiles = SettingsLoader.LoadPrompts();
                    MuxAssert.AreEqual(1, profiles.Count, "profile survived operational save");
                    MuxAssert.AreEqual("persona", profiles[0].SystemPrompt, "profile intact");
                    MuxAssert.AreEqual("op-override", SettingsLoader.LoadOperationalPrompts()["title.system"], "override intact");

                    // Saving profiles again must not drop the operational map.
                    SettingsLoader.SavePrompts(SettingsLoader.LoadPrompts());
                    MuxAssert.IsTrue(SettingsLoader.LoadOperationalPrompts().ContainsKey("title.system"), "override survived profile save");
                    return Task.CompletedTask;
                }),

                Case("WireStringRoundTrips", "Every kind maps to a stable wire string and back", (string dir, CancellationToken ct) =>
                {
                    foreach (PromptKind kind in (PromptKind[])Enum.GetValues(typeof(PromptKind)))
                    {
                        string wire = kind.ToWireString();
                        MuxAssert.IsFalse(string.IsNullOrWhiteSpace(wire), "wire non-empty: " + kind);
                        MuxAssert.IsTrue(PromptKindExtensions.TryParseWireString(wire, out PromptKind parsed), "parses: " + wire);
                        MuxAssert.AreEqual(kind, parsed, "round trip: " + kind);
                    }
                    MuxAssert.IsFalse(PromptKindExtensions.TryParseWireString("bogus", out _), "bogus rejected");
                    return Task.CompletedTask;
                })
            };

            return new TestSuiteDescriptor("PromptCatalog", "Prompt catalog, resolver, overrides, and validation", cases);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("PromptCatalog", caseId, displayName, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_prompt_" + Guid.NewGuid().ToString("N"));
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
    }
}
