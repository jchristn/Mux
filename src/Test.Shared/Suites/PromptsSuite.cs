namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Touchstone.Core;
    using TUIKit.Input;

    /// <summary>
    /// Touchstone suite for the prompt-profile feature: the reusable <see cref="CommandRuntimeResolver"/>
    /// prompt substitution/fallbacks and the <see cref="PromptEditorModal"/> interactions (switch, activate,
    /// add, remove, edit, field switch).
    /// </summary>
    public static class PromptsSuite
    {
        private const string SuiteId = "Prompts";

        /// <summary>
        /// Builds the prompts suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the prompts cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Prompt-profile resolution and the prompt editor modal",
                new List<TestCaseDescriptor>
                {
                    // ---- CommandRuntimeResolver.ResolveProfilePrompts ----
                    Case("ResolveSubstitutesPlaceholdersWhenToolsEnabled", "Placeholders are substituted when tools are enabled", ct =>
                    {
                        PromptProfile profile = new PromptProfile { SystemPrompt = "cwd={WorkingDirectory} tools={ToolDescriptions}" };
                        List<ToolDefinition> tools = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "foo", Description = "does foo" }
                        };

                        (string system, string compaction) = CommandRuntimeResolver.ResolveProfilePrompts(profile, true, "/work", tools);
                        MuxAssert.AreEqual("cwd=/work tools=- foo: does foo", system, "substituted system prompt");
                        MuxAssert.AreEqual(Defaults.CompactionSystemPrompt, compaction, "compaction falls back to default");
                        return Task.CompletedTask;
                    }),

                    Case("ResolveUsesToolsDisabledPromptWhenToolsDisabled", "The tools-disabled prompt is used with no tool listing when tools are disabled", ct =>
                    {
                        PromptProfile profile = new PromptProfile { ToolsDisabledPrompt = "no tools, cwd={WorkingDirectory}" };
                        (string system, _) = CommandRuntimeResolver.ResolveProfilePrompts(profile, false, "/w", new List<ToolDefinition>());
                        MuxAssert.AreEqual("no tools, cwd=/w", system, "tools-disabled prompt substituted");
                        return Task.CompletedTask;
                    }),

                    Case("ResolveFallsBackToBuiltInDefaults", "Empty profile fields fall back to the built-in defaults", ct =>
                    {
                        (string system, string compaction) = CommandRuntimeResolver.ResolveProfilePrompts(new PromptProfile(), true, "/w", new List<ToolDefinition>());
                        MuxAssert.Contains("You are mux", system, "default system prompt used");
                        MuxAssert.Contains("/w", system, "working directory substituted");
                        MuxAssert.IsFalse(system.Contains("{WorkingDirectory}"), "working directory placeholder substituted");
                        MuxAssert.AreEqual(Defaults.CompactionSystemPrompt, compaction, "default compaction");
                        return Task.CompletedTask;
                    }),

                    Case("ResolveUsesProfileCompactionPrompt", "A custom compaction prompt is returned verbatim", ct =>
                    {
                        PromptProfile profile = new PromptProfile { CompactionPrompt = "summarize tersely" };
                        (_, string compaction) = CommandRuntimeResolver.ResolveProfilePrompts(profile, true, "/w", new List<ToolDefinition>());
                        MuxAssert.AreEqual("summarize tersely", compaction, "custom compaction prompt");
                        return Task.CompletedTask;
                    }),

                    Case("ResolveIncludesTaskGuidanceWhenPlanToolPresent", "The default prompt includes task-planning guidance when plan_tasks is offered", ct =>
                    {
                        List<ToolDefinition> tools = new List<ToolDefinition>
                        {
                            new ToolDefinition { Name = "plan_tasks", Description = "plan" },
                            new ToolDefinition { Name = "update_task", Description = "update" }
                        };
                        (string system, _) = CommandRuntimeResolver.ResolveProfilePrompts(new PromptProfile(), true, "/w", tools);
                        MuxAssert.Contains("Task planning:", system, "guidance present");
                        MuxAssert.IsFalse(system.Contains("{TaskPlanningGuidance}"), "placeholder substituted");
                        return Task.CompletedTask;
                    }),

                    Case("ResolveOmitsTaskGuidanceWithoutPlanTool", "The default prompt omits task-planning guidance when plan_tasks is absent", ct =>
                    {
                        (string system, _) = CommandRuntimeResolver.ResolveProfilePrompts(new PromptProfile(), true, "/w", new List<ToolDefinition> { new ToolDefinition { Name = "read_file", Description = "read" } });
                        MuxAssert.IsFalse(system.Contains("Task planning:"), "guidance omitted");
                        MuxAssert.IsFalse(system.Contains("{TaskPlanningGuidance}"), "placeholder substituted");
                        return Task.CompletedTask;
                    }),

                    // ---- PromptEditorModal ----
                    Case("ModalActivatesSelectedProfile", "Space activates the selected profile", async ct =>
                    {
                        PromptEditorModal modal = new PromptEditorModal(new List<PromptProfile>
                        {
                            new PromptProfile { Name = "A", IsActive = true },
                            new PromptProfile { Name = "B" }
                        });

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right)); // select B
                        modal.HandleKey(KeyEvent.Char((int)' '));         // activate B
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        List<PromptProfile> result = (List<PromptProfile>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.IsFalse(result[0].IsActive, "A no longer active");
                        MuxAssert.IsTrue(result[1].IsActive, "B active");
                    }),

                    Case("ModalAddsProfile", "Pressing 'a', naming, and Enter adds a profile", async ct =>
                    {
                        PromptEditorModal modal = new PromptEditorModal(new List<PromptProfile>
                        {
                            new PromptProfile { Name = "A", IsActive = true }
                        });

                        modal.HandleKey(KeyEvent.Char((int)'a')); // begin add (naming)
                        modal.HandleKey(KeyEvent.Char((int)'N'));
                        modal.HandleKey(KeyEvent.Char((int)'e'));
                        modal.HandleKey(KeyEvent.Char((int)'w'));
                        modal.HandleKey(KeyEvent.Special(KeyCode.Enter)); // commit name "New"
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        List<PromptProfile> result = (List<PromptProfile>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.AreEqual(2, result.Count, "profile added");
                        MuxAssert.AreEqual("New", result[1].Name, "new profile name");
                    }),

                    Case("ModalRemovesProfile", "Pressing 'x' removes the selected profile", async ct =>
                    {
                        PromptEditorModal modal = new PromptEditorModal(new List<PromptProfile>
                        {
                            new PromptProfile { Name = "A", IsActive = true },
                            new PromptProfile { Name = "B" }
                        });

                        modal.HandleKey(KeyEvent.Special(KeyCode.Right)); // select B
                        modal.HandleKey(KeyEvent.Char((int)'x'));         // remove B
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape));

                        List<PromptProfile> result = (List<PromptProfile>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.AreEqual(1, result.Count, "one profile remains");
                        MuxAssert.AreEqual("A", result[0].Name, "A remains");
                    }),

                    Case("ModalEditsSelectedFieldOnly", "Editing writes to the current field and leaves others untouched", async ct =>
                    {
                        PromptEditorModal modal = new PromptEditorModal(new List<PromptProfile>
                        {
                            new PromptProfile { Name = "A", IsActive = true, SystemPrompt = "S", ToolsDisabledPrompt = "T" }
                        });

                        // Switch to the Tools-disabled field, edit it, then close.
                        modal.HandleKey(KeyEvent.Special(KeyCode.Tab));   // System -> ToolsDisabled
                        modal.HandleKey(KeyEvent.Char((int)'e'));         // begin edit
                        modal.HandleKey(KeyEvent.Char((int)'Z'));         // insert Z
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape)); // stop editing (flush)
                        modal.HandleKey(KeyEvent.Special(KeyCode.Escape)); // close

                        List<PromptProfile> result = (List<PromptProfile>)(await modal.Completion.ConfigureAwait(false))!;
                        MuxAssert.AreEqual("S", result[0].SystemPrompt, "system prompt unchanged");
                        MuxAssert.Contains("Z", result[0].ToolsDisabledPrompt, "tools-disabled prompt edited");
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
