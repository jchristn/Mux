namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Subagents;
    using Mux.Core.Tools;
    using Mux.Core.Tools.Tools;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for subagent delegation: the <see cref="SubagentRegistry"/> validity/dedup rules,
    /// the <see cref="SpawnSubagentTool"/> routing and error contract (driven by a fake executor so no live
    /// LLM is needed), the conditional registration of <c>spawn_subagent</c> in
    /// <see cref="BuiltInToolRegistry"/>, and the <c>subagents.json</c> load/save round trip.
    /// </summary>
    public static class SubagentSuite
    {
        private const string SuiteId = "Subagent";

        /// <summary>
        /// Builds the subagent suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the subagent cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Subagent registry, spawn tool, and persistence",
                new List<TestCaseDescriptor>
                {
                    Case("DefaultSubagentsAreValidAndCoverLifecycle", "The default subagent set is all-valid and covers the product lifecycle roles", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        List<SubagentDefinition> defaults = DefaultSubagents.Build();
                        foreach (SubagentDefinition d in defaults)
                        {
                            MuxAssert.IsTrue(SubagentRegistry.IsValid(d), d.Name + " is valid");
                            MuxAssert.IsTrue(!string.IsNullOrWhiteSpace(d.Description), d.Name + " has a description");
                        }

                        SubagentRegistry registry = new SubagentRegistry(defaults);
                        foreach (string role in new[] { "product-manager", "architect", "software-engineer", "test-engineer", "ux-engineer", "experience-evaluator", "code-reviewer", "devops-engineer" })
                        {
                            MuxAssert.IsNotNull(registry.Find(role), "default includes " + role);
                        }

                        // Review/planning personas are read-only (no write/edit/delete tools).
                        SubagentDefinition reviewer = registry.Find("code-reviewer")!;
                        MuxAssert.IsFalse(reviewer.AllowedTools.Contains("write_file"), "code-reviewer cannot write");
                        MuxAssert.IsFalse(reviewer.AllowedTools.Contains("edit_file"), "code-reviewer cannot edit");
                    }),

                    Case("RegistryDropsInvalidAndDeduplicates", "The registry drops invalid definitions and keeps the first of a duplicated name", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        SubagentRegistry registry = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Review." },
                            new SubagentDefinition { Name = "", SystemPrompt = "No name." },
                            new SubagentDefinition { Name = "blank", SystemPrompt = "   " },
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Second reviewer." }
                        });

                        MuxAssert.AreEqual(1, registry.Count, "only the first valid reviewer survives");
                        MuxAssert.AreEqual("Review.", registry.Find("reviewer")?.SystemPrompt, "first definition kept");
                    }),

                    Case("RegistryFindIsCaseInsensitive", "The registry resolves a subagent name case-insensitively", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        SubagentRegistry registry = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "Reviewer", SystemPrompt = "Review." }
                        });

                        MuxAssert.IsNotNull(registry.Find("reviewer"), "lower-case resolves");
                        MuxAssert.IsNotNull(registry.Find("REVIEWER"), "upper-case resolves");
                        MuxAssert.IsTrue(registry.Find("missing") == null, "unknown is null");
                    }),

                    Case("SpawnToolRoutesToExecutorAndReturnsResult", "spawn_subagent runs the named subagent and returns its final text", async (CancellationToken ct) =>
                    {
                        RecordingExecutor executor = new RecordingExecutor("Reviewed: found 2 issues.", success: true);
                        SubagentRegistry registry = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Review." }
                        });

                        SpawnSubagentTool tool = new SpawnSubagentTool(registry, executor);
                        JsonElement args = ToolArgs.From(new { subagent = "reviewer", prompt = "Review file X." });
                        ToolResult result = await tool.ExecuteAsync("call-1", args, "/work", ct).ConfigureAwait(false);

                        MuxAssert.IsTrue(result.Success, "tool reports success");
                        MuxAssert.AreEqual("reviewer", executor.LastDefinition?.Name, "routed to the reviewer");
                        MuxAssert.AreEqual("Review file X.", executor.LastPrompt, "prompt forwarded verbatim");
                        MuxAssert.Contains("found 2 issues", result.Content, "final text surfaced");
                    }),

                    Case("SpawnToolUnknownSubagentErrors", "spawn_subagent reports an unknown subagent with the available names", async (CancellationToken ct) =>
                    {
                        RecordingExecutor executor = new RecordingExecutor("unused", success: true);
                        SubagentRegistry registry = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Review." }
                        });

                        SpawnSubagentTool tool = new SpawnSubagentTool(registry, executor);
                        JsonElement args = ToolArgs.From(new { subagent = "nonesuch", prompt = "do it" });
                        ToolResult result = await tool.ExecuteAsync("call-2", args, "/work", ct).ConfigureAwait(false);

                        MuxAssert.IsFalse(result.Success, "unknown subagent fails");
                        MuxAssert.Contains("unknown_subagent", result.Content, "error code present");
                        MuxAssert.Contains("reviewer", result.Content, "available names listed");
                        MuxAssert.IsTrue(executor.CallCount == 0, "executor never invoked");
                    }),

                    Case("SpawnToolMissingPromptErrors", "spawn_subagent requires a prompt", async (CancellationToken ct) =>
                    {
                        RecordingExecutor executor = new RecordingExecutor("unused", success: true);
                        SubagentRegistry registry = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Review." }
                        });

                        SpawnSubagentTool tool = new SpawnSubagentTool(registry, executor);
                        JsonElement args = ToolArgs.From(new { subagent = "reviewer" });
                        ToolResult result = await tool.ExecuteAsync("call-3", args, "/work", ct).ConfigureAwait(false);

                        MuxAssert.IsFalse(result.Success, "missing prompt fails");
                        MuxAssert.Contains("missing_prompt", result.Content, "error code present");
                    }),

                    Case("SpawnToolPropagatesExecutorFailure", "spawn_subagent surfaces a failed subagent run", async (CancellationToken ct) =>
                    {
                        RecordingExecutor executor = new RecordingExecutor(string.Empty, success: false, error: "model refused");
                        SubagentRegistry registry = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Review." }
                        });

                        SpawnSubagentTool tool = new SpawnSubagentTool(registry, executor);
                        JsonElement args = ToolArgs.From(new { subagent = "reviewer", prompt = "go" });
                        ToolResult result = await tool.ExecuteAsync("call-4", args, "/work", ct).ConfigureAwait(false);

                        MuxAssert.IsFalse(result.Success, "failed run reported as failure");
                        MuxAssert.Contains("model refused", result.Content, "error text surfaced");
                    }),

                    Case("RegistryRegistersSpawnToolOnlyWhenConfigured", "BuiltInToolRegistry registers spawn_subagent only with a non-empty registry and executor", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxSettings settings = new MuxSettings();

                        BuiltInToolRegistry none = new BuiltInToolRegistry(settings);
                        MuxAssert.IsFalse(none.HasTool("spawn_subagent"), "not registered without subagents");

                        SubagentRegistry empty = new SubagentRegistry(new List<SubagentDefinition>());
                        BuiltInToolRegistry withEmpty = new BuiltInToolRegistry(settings, null, empty, new RecordingExecutor("x", true));
                        MuxAssert.IsFalse(withEmpty.HasTool("spawn_subagent"), "not registered with an empty registry");

                        SubagentRegistry populated = new SubagentRegistry(new List<SubagentDefinition>
                        {
                            new SubagentDefinition { Name = "reviewer", SystemPrompt = "Review." }
                        });
                        BuiltInToolRegistry withBoth = new BuiltInToolRegistry(settings, null, populated, new RecordingExecutor("x", true));
                        MuxAssert.IsTrue(withBoth.HasTool("spawn_subagent"), "registered when both supplied");
                        MuxAssert.AreEqual(ToolMutationKind.ReadOnly, withBoth.GetMutationKind("spawn_subagent"), "read-only w.r.t. the write lease");
                    }),

                    SettingsCase("SubagentsRoundTripThroughDisk", "subagents.json saves and loads definitions", (string dir, CancellationToken ct) =>
                    {
                        List<SubagentDefinition> defs = new List<SubagentDefinition>
                        {
                            new SubagentDefinition
                            {
                                Name = "reviewer",
                                Description = "Reviews code.",
                                SystemPrompt = "You review.",
                                EndpointName = "fast",
                                AllowedTools = new List<string> { "read_file", "grep" },
                                MaxIterations = 8
                            }
                        };

                        SettingsLoader.SaveSubagents(defs);
                        List<SubagentDefinition> loaded = SettingsLoader.LoadSubagents();

                        MuxAssert.AreEqual(1, loaded.Count, "one definition round-trips");
                        MuxAssert.AreEqual("reviewer", loaded[0].Name, "name");
                        MuxAssert.AreEqual("fast", loaded[0].EndpointName, "endpoint override");
                        MuxAssert.AreEqual(2, loaded[0].AllowedTools.Count, "allowed tools");
                        MuxAssert.AreEqual(8, loaded[0].MaxIterations, "max iterations");
                        return Task.CompletedTask;
                    }),

                    SettingsCase("EnsureConfigDirectorySeedsExampleSubagent", "EnsureConfigDirectory seeds an example subagents.json", (string dir, CancellationToken ct) =>
                    {
                        SettingsLoader.EnsureConfigDirectory();
                        MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "subagents.json")), "seeded file exists");
                        List<SubagentDefinition> loaded = SettingsLoader.LoadSubagents();
                        MuxAssert.IsTrue(loaded.Count >= 1, "seeded at least one example");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static TestCaseDescriptor SettingsCase(string id, string name, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_subagent_" + Guid.NewGuid().ToString("N"));
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

        private sealed class RecordingExecutor : ISubagentExecutor
        {
            private readonly string _Text;
            private readonly bool _Success;
            private readonly string? _Error;

            public RecordingExecutor(string text, bool success, string? error = null)
            {
                _Text = text;
                _Success = success;
                _Error = error;
            }

            public SubagentDefinition? LastDefinition { get; private set; }

            public string? LastPrompt { get; private set; }

            public int CallCount { get; private set; }

            public Task<SubagentResult> ExecuteAsync(SubagentDefinition definition, string prompt, string workingDirectory, CancellationToken cancellationToken)
            {
                CallCount++;
                LastDefinition = definition;
                LastPrompt = prompt;
                return Task.FromResult(new SubagentResult
                {
                    Success = _Success,
                    FinalText = _Text,
                    Iterations = 1,
                    Error = _Error
                });
            }
        }

        #endregion
    }
}
