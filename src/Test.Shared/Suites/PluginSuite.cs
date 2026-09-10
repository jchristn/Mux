namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Plugins;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the plugin system: the <see cref="PluginRegistry"/> validity/dedup rules and
    /// event filtering, the <see cref="HookEventEnumConverter"/> parse/write forms, the out-of-process
    /// <see cref="HookRunner"/> (success, missing command, veto, stdin delivery), the
    /// <see cref="CustomCommandRunner"/>, and the <c>hooks.json</c> load/save round trip. Process cases drive
    /// the always-available <c>git</c> executable and skip if git is absent.
    /// </summary>
    public static class PluginSuite
    {
        private const string SuiteId = "Plugin";

        /// <summary>
        /// Builds the plugin suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the plugin cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Event hooks and custom commands",
                new List<TestCaseDescriptor>
                {
                    Case("RegistryDropsInvalidAndDedupes", "The registry drops invalid entries and duplicate command names", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        PluginConfig config = new PluginConfig
                        {
                            Hooks = new List<HookDefinition>
                            {
                                new HookDefinition { Command = "git", Event = HookEventEnum.SessionStart },
                                new HookDefinition { Command = "", Event = HookEventEnum.SessionEnd }
                            },
                            Commands = new List<CustomCommandDefinition>
                            {
                                new CustomCommandDefinition { Name = "deploy", Command = "git" },
                                new CustomCommandDefinition { Name = "", Command = "git" },
                                new CustomCommandDefinition { Name = "deploy", Command = "git" }
                            }
                        };

                        PluginRegistry registry = new PluginRegistry(config);
                        MuxAssert.AreEqual(1, registry.Hooks.Count, "invalid hook dropped");
                        MuxAssert.AreEqual(1, registry.Commands.Count, "empty-name and duplicate commands dropped");
                        MuxAssert.IsNotNull(registry.FindCommand("/deploy"), "leading slash tolerated in lookup");
                    }),

                    Case("RegistryFiltersByEvent", "HooksFor returns only the matching event's hooks", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        PluginRegistry registry = new PluginRegistry(new PluginConfig
                        {
                            Hooks = new List<HookDefinition>
                            {
                                new HookDefinition { Command = "git", Event = HookEventEnum.SessionStart },
                                new HookDefinition { Command = "git", Event = HookEventEnum.UserPromptSubmit }
                            }
                        });

                        MuxAssert.AreEqual(1, registry.HooksFor(HookEventEnum.SessionStart).Count, "one session-start hook");
                        MuxAssert.AreEqual(1, registry.HooksFor(HookEventEnum.UserPromptSubmit).Count, "one prompt-submit hook");
                        MuxAssert.AreEqual(0, registry.HooksFor(HookEventEnum.SessionEnd).Count, "no session-end hooks");
                    }),

                    Case("EventEnumParsesForms", "The hook-event converter parses kebab, snake, and member-name forms", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxAssert.IsTrue(HookEventEnumConverter.TryParse("session-start", out HookEventEnum a) && a == HookEventEnum.SessionStart, "kebab");
                        MuxAssert.IsTrue(HookEventEnumConverter.TryParse("user_prompt_submit", out HookEventEnum b) && b == HookEventEnum.UserPromptSubmit, "snake");
                        MuxAssert.IsTrue(HookEventEnumConverter.TryParse("SessionEnd", out HookEventEnum c) && c == HookEventEnum.SessionEnd, "member name");
                        MuxAssert.IsFalse(HookEventEnumConverter.TryParse("bogus", out _), "unknown rejected");
                        MuxAssert.AreEqual("user-prompt-submit", HookEventEnumConverter.ToWireName(HookEventEnum.UserPromptSubmit), "wire name");
                    }),

                    Case("VetoableEvents", "Only user-prompt-submit is vetoable", async (CancellationToken ct) =>
                    {
                        await Task.CompletedTask.ConfigureAwait(false);
                        MuxAssert.IsTrue(HookRunner.IsVetoable(HookEventEnum.UserPromptSubmit), "prompt submit vetoable");
                        MuxAssert.IsFalse(HookRunner.IsVetoable(HookEventEnum.SessionStart), "session start not vetoable");
                    }),

                    ProcessCase("HookRunsAndCapturesOutput", "A hook runs out-of-process and its stdout is captured", async (CancellationToken ct) =>
                    {
                        PluginRegistry registry = new PluginRegistry(new PluginConfig
                        {
                            Hooks = new List<HookDefinition>
                            {
                                new HookDefinition { Name = "version", Command = "git", Args = new List<string> { "--version" }, Event = HookEventEnum.SessionStart }
                            }
                        });

                        IReadOnlyList<HookRunResult> results = await new HookRunner()
                            .RunAsync(registry, HookEventEnum.SessionStart, null, Directory.GetCurrentDirectory(), ct)
                            .ConfigureAwait(false);

                        MuxAssert.AreEqual(1, results.Count, "one hook ran");
                        MuxAssert.AreEqual(0, results[0].ExitCode, "hook succeeded");
                        MuxAssert.Contains("git version", results[0].StdOut, "stdout captured");
                    }),

                    ProcessCase("MissingHookCommandDoesNotThrow", "A hook whose command is missing reports not-started instead of throwing", async (CancellationToken ct) =>
                    {
                        PluginRegistry registry = new PluginRegistry(new PluginConfig
                        {
                            Hooks = new List<HookDefinition>
                            {
                                new HookDefinition { Command = "mux-nonexistent-binary-xyz", Event = HookEventEnum.SessionStart }
                            }
                        });

                        IReadOnlyList<HookRunResult> results = await new HookRunner()
                            .RunAsync(registry, HookEventEnum.SessionStart, null, Directory.GetCurrentDirectory(), ct)
                            .ConfigureAwait(false);

                        MuxAssert.AreEqual(1, results.Count, "one result");
                        MuxAssert.IsFalse(results[0].Started, "process did not start");
                        MuxAssert.IsFalse(results[0].Vetoed, "a broken hook does not veto");
                    }),

                    ProcessCase("BlockingHookVetoesOnNonZeroExit", "A blocking prompt-submit hook that exits non-zero vetoes", async (CancellationToken ct) =>
                    {
                        string nonRepo = Path.Combine(Path.GetTempPath(), "mux_plugin_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(nonRepo);
                        try
                        {
                            PluginRegistry registry = new PluginRegistry(new PluginConfig
                            {
                                Hooks = new List<HookDefinition>
                                {
                                    // 'git rev-parse --verify HEAD' exits non-zero outside a repository.
                                    new HookDefinition { Command = "git", Args = new List<string> { "rev-parse", "--verify", "HEAD" }, Event = HookEventEnum.UserPromptSubmit, Blocking = true }
                                }
                            });

                            IReadOnlyList<HookRunResult> results = await new HookRunner()
                                .RunAsync(registry, HookEventEnum.UserPromptSubmit, "{}", nonRepo, ct)
                                .ConfigureAwait(false);

                            MuxAssert.IsTrue(HookRunner.WasVetoed(results), "blocking non-zero hook vetoes");
                        }
                        finally
                        {
                            TryDeleteDirectory(nonRepo);
                        }
                    }),

                    ProcessCase("NonBlockingHookDoesNotVeto", "A non-blocking hook that exits non-zero does not veto", async (CancellationToken ct) =>
                    {
                        string nonRepo = Path.Combine(Path.GetTempPath(), "mux_plugin_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(nonRepo);
                        try
                        {
                            PluginRegistry registry = new PluginRegistry(new PluginConfig
                            {
                                Hooks = new List<HookDefinition>
                                {
                                    new HookDefinition { Command = "git", Args = new List<string> { "rev-parse", "--verify", "HEAD" }, Event = HookEventEnum.UserPromptSubmit, Blocking = false }
                                }
                            });

                            IReadOnlyList<HookRunResult> results = await new HookRunner()
                                .RunAsync(registry, HookEventEnum.UserPromptSubmit, "{}", nonRepo, ct)
                                .ConfigureAwait(false);

                            MuxAssert.IsFalse(HookRunner.WasVetoed(results), "non-blocking hook never vetoes");
                        }
                        finally
                        {
                            TryDeleteDirectory(nonRepo);
                        }
                    }),

                    ProcessCase("HookReceivesPayloadOnStdin", "A hook receives the event payload on stdin", async (CancellationToken ct) =>
                    {
                        PluginRegistry registry = new PluginRegistry(new PluginConfig
                        {
                            Hooks = new List<HookDefinition>
                            {
                                // git hash-object --stdin reads stdin and prints the blob's 40-char sha.
                                new HookDefinition { Command = "git", Args = new List<string> { "hash-object", "--stdin" }, Event = HookEventEnum.SessionStart }
                            }
                        });

                        IReadOnlyList<HookRunResult> results = await new HookRunner()
                            .RunAsync(registry, HookEventEnum.SessionStart, "payload-content", Directory.GetCurrentDirectory(), ct)
                            .ConfigureAwait(false);

                        MuxAssert.AreEqual(0, results[0].ExitCode, "hash-object succeeded");
                        MuxAssert.AreEqual(40, results[0].StdOut.Trim().Length, "stdin was hashed to a 40-char sha");
                    }),

                    ProcessCase("CustomCommandRuns", "A custom command runs and captures output", async (CancellationToken ct) =>
                    {
                        CustomCommandDefinition definition = new CustomCommandDefinition { Name = "ver", Command = "git", Args = new List<string> { "--version" } };
                        HookRunResult result = await new CustomCommandRunner().RunAsync(definition, Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, result.ExitCode, "command succeeded");
                        MuxAssert.Contains("git version", result.StdOut, "output captured");
                    }),

                    SettingsCase("PluginConfigRoundTrips", "hooks.json saves and loads hooks and commands", (string dir, CancellationToken ct) =>
                    {
                        PluginConfig config = new PluginConfig
                        {
                            Hooks = new List<HookDefinition>
                            {
                                new HookDefinition { Name = "lint", Command = "dotnet", Args = new List<string> { "format" }, Event = HookEventEnum.UserPromptSubmit, Blocking = true, TimeoutMs = 5000 }
                            },
                            Commands = new List<CustomCommandDefinition>
                            {
                                new CustomCommandDefinition { Name = "status", Description = "git status", Command = "git", Args = new List<string> { "status" } }
                            }
                        };

                        SettingsLoader.SavePluginConfig(config);
                        PluginConfig loaded = SettingsLoader.LoadPluginConfig();

                        MuxAssert.AreEqual(1, loaded.Hooks.Count, "hook persisted");
                        MuxAssert.AreEqual(HookEventEnum.UserPromptSubmit, loaded.Hooks[0].Event, "event round-trips");
                        MuxAssert.IsTrue(loaded.Hooks[0].Blocking, "blocking round-trips");
                        MuxAssert.AreEqual(1, loaded.Commands.Count, "command persisted");
                        MuxAssert.AreEqual("status", loaded.Commands[0].Name, "command name");
                        return Task.CompletedTask;
                    }),

                    SettingsCase("EnsureConfigDirectorySeedsHooks", "EnsureConfigDirectory seeds an empty hooks.json", (string dir, CancellationToken ct) =>
                    {
                        SettingsLoader.EnsureConfigDirectory();
                        MuxAssert.IsTrue(File.Exists(Path.Combine(dir, "hooks.json")), "seeded file exists");
                        PluginRegistry registry = new PluginRegistry(SettingsLoader.LoadPluginConfig());
                        MuxAssert.IsTrue(registry.IsEmpty, "seeded config is empty");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static TestCaseDescriptor ProcessCase(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                if (!IsGitAvailable())
                {
                    return;
                }

                await body(ct).ConfigureAwait(false);
            });
        }

        private static TestCaseDescriptor SettingsCase(string id, string name, Func<string, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "mux_plugincfg_" + Guid.NewGuid().ToString("N"));
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
                    TryDeleteDirectory(tempDir);
                }
            });
        }

        private static bool IsGitAvailable()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("--version");
                using Process process = Process.Start(startInfo)!;
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void TryDeleteDirectory(string dir)
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
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
