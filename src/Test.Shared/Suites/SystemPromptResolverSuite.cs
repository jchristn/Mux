namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SystemPromptResolver"/> — the shared system-prompt resolution the CLI
    /// and desktop both delegate to. Cases assert placeholder substitution (working directory, tool
    /// descriptions, task-planning guidance), the tools-disabled prompt variant and its built-in fallback,
    /// the append-after-substitution behavior, and compaction-prompt selection.
    /// </summary>
    public static class SystemPromptResolverSuite
    {
        private const string SuiteId = "SystemPromptResolver";

        /// <summary>
        /// Builds the system-prompt-resolver suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for resolver cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Shared system-prompt resolution",
                new List<TestCaseDescriptor>
                {
                    Case("ToolsEnabledSubstitutesAllPlaceholders", "Tools enabled substitutes working dir, tool descriptions, and task-planning guidance", (CancellationToken ct) =>
                    {
                        PromptProfile profile = new PromptProfile { CompactionPrompt = "compact me" };
                        List<ToolDefinition> tools = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "read_file", Description = "reads a file" }
                        };

                        ResolvedSystemPrompt r = SystemPromptResolver.Resolve(
                            "wd={WorkingDirectory} tools={ToolDescriptions} tp={TaskPlanningGuidance}",
                            profile, toolsEnabled: true, tools, "/work", taskPlanningEnabled: true, appendSystemPrompt: null);

                        MuxAssert.Contains("wd=/work", r.SystemPrompt, "working directory substituted");
                        MuxAssert.Contains("- read_file: reads a file", r.SystemPrompt, "tool descriptions filled");
                        MuxAssert.Contains("Task planning:", r.SystemPrompt, "task-planning guidance injected");
                        MuxAssert.AreEqual("compact me", r.CompactionSystemPrompt, "compaction from profile");
                        return Task.CompletedTask;
                    }),

                    Case("TaskPlanningOffOmitsGuidance", "Task planning disabled leaves the guidance placeholder empty", (CancellationToken ct) =>
                    {
                        ResolvedSystemPrompt r = SystemPromptResolver.Resolve(
                            "before[{TaskPlanningGuidance}]after",
                            new PromptProfile(), toolsEnabled: true, new List<ToolDefinition>(), "/w", taskPlanningEnabled: false, appendSystemPrompt: null);

                        MuxAssert.AreEqual("before[]after", r.SystemPrompt, "guidance placeholder blanked");
                        return Task.CompletedTask;
                    }),

                    Case("ToolsDisabledUsesProfileVariant", "Tools disabled uses the profile's tools-disabled prompt and fills no tool descriptions", (CancellationToken ct) =>
                    {
                        PromptProfile profile = new PromptProfile { ToolsDisabledPrompt = "no tools in {WorkingDirectory}" };
                        List<ToolDefinition> tools = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "read_file", Description = "reads a file" }
                        };

                        ResolvedSystemPrompt r = SystemPromptResolver.Resolve(
                            "SHOULD NOT BE USED {ToolDescriptions}",
                            profile, toolsEnabled: false, tools, "/work", taskPlanningEnabled: true, appendSystemPrompt: null);

                        MuxAssert.AreEqual("no tools in /work", r.SystemPrompt, "tools-disabled prompt used, no tool descriptions");
                        return Task.CompletedTask;
                    }),

                    Case("ToolsDisabledFallsBackToDefault", "Tools disabled with an empty profile prompt falls back to the built-in default", (CancellationToken ct) =>
                    {
                        ResolvedSystemPrompt r = SystemPromptResolver.Resolve(
                            "ignored", new PromptProfile(), toolsEnabled: false, null, "/work", taskPlanningEnabled: false, appendSystemPrompt: null);

                        string expected = Defaults.ToolsDisabledSystemPrompt.Replace("{WorkingDirectory}", "/work");
                        MuxAssert.AreEqual(expected, r.SystemPrompt, "falls back to the built-in tools-disabled prompt");
                        return Task.CompletedTask;
                    }),

                    Case("AppendAddsAfterSubstitution", "Appended text is added after all substitution", (CancellationToken ct) =>
                    {
                        ResolvedSystemPrompt r = SystemPromptResolver.Resolve(
                            "base", new PromptProfile(), toolsEnabled: true, new List<ToolDefinition>(), "/w", taskPlanningEnabled: false, appendSystemPrompt: "  EXTRA  ");

                        MuxAssert.IsTrue(r.SystemPrompt.StartsWith("base"), "base prompt preserved");
                        MuxAssert.IsTrue(r.SystemPrompt.EndsWith("EXTRA"), "appended text trimmed and added at the end");
                        return Task.CompletedTask;
                    }),

                    Case("EmptyCompactionInheritsEmpty", "A blank profile compaction prompt resolves to empty (loop inherits the default)", (CancellationToken ct) =>
                    {
                        ResolvedSystemPrompt r = SystemPromptResolver.Resolve(
                            "base", new PromptProfile(), toolsEnabled: true, null, "/w", taskPlanningEnabled: false, appendSystemPrompt: null);

                        MuxAssert.AreEqual(string.Empty, r.CompactionSystemPrompt, "empty compaction inherits");
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
