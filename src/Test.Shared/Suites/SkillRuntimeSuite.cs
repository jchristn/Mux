namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SkillRuntime"/>: it discovers skills, exposes the two tools as an
    /// external provider, applies the index enablement, builds the prompt section, runs a skill, and
    /// disposes cleanly.
    /// </summary>
    public static class SkillRuntimeSuite
    {
        private const string SuiteId = "SkillRuntime";

        /// <summary>
        /// Builds the skill-runtime suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the runtime cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Skill runtime discovery, provider, and lifecycle",
                new List<TestCaseDescriptor>
                {
                    Case("EmptyRuntimeExposesNoTools", "With no skills the runtime exposes no tools or prompt", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            using (SkillRuntime runtime = new SkillRuntime(root, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromMilliseconds(50)))
                            {
                                runtime.Start();
                                await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(0, runtime.GetToolDefinitions().Count, "no tools");
                                MuxAssert.AreEqual(0, runtime.GetStatus().Count, "no status");
                                MuxAssert.AreEqual(string.Empty, runtime.BuildPromptSection(), "no prompt section");
                            }
                        })),

                    Case("DiscoversExposesAndRunsSkill", "The runtime discovers a skill, exposes it, and runs it", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            WriteNodeEchoSkill(root, "echoer");

                            int changes = 0;
                            using (SkillRuntime runtime = new SkillRuntime(root, () => new List<SkillIndexEntry>(), () => Interlocked.Increment(ref changes), TimeSpan.FromMilliseconds(50)))
                            {
                                runtime.Start();
                                await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(2, runtime.GetToolDefinitions().Count, "two skill tools exposed");
                                MuxAssert.AreEqual(1, runtime.GetStatus().Count, "one skill in status");
                                MuxAssert.Contains("echoer", runtime.BuildPromptSection(), "skill listed in prompt");
                                MuxAssert.IsTrue(changes >= 1, "skills-changed fired");

                                string json = JsonSerializer.Serialize(new { name = "echoer", command = "say" });
                                using (JsonDocument doc = JsonDocument.Parse(json))
                                {
                                    ToolResult result = await runtime.ExecuteAsync("run_skill", doc.RootElement, root, ct).ConfigureAwait(false);
                                    MuxAssert.IsTrue(result.Success, "skill ran: " + result.Content);
                                    MuxAssert.Contains("SKILL_BLOCK_MARKER", result.Content, "skill output captured");
                                }
                            }
                        })),

                    Case("IndexDisablesSkill", "A skill disabled in the index is not exposed", (CancellationToken ct) =>
                        WithSkillsDirAsync(async (root) =>
                        {
                            WriteNodeEchoSkill(root, "echoer");
                            List<SkillIndexEntry> index = new List<SkillIndexEntry> { new SkillIndexEntry { Id = "echoer", Enabled = false } };

                            using (SkillRuntime runtime = new SkillRuntime(root, () => index, () => { }, TimeSpan.FromMilliseconds(50)))
                            {
                                runtime.Start();
                                await runtime.FirstRefreshCompleted.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);

                                MuxAssert.AreEqual(0, runtime.GetToolDefinitions().Count, "disabled skill exposes no tools");
                                MuxAssert.AreEqual(1, runtime.GetStatus().Count, "still listed in status");
                                MuxAssert.IsFalse(runtime.GetStatus()[0].Enabled, "status reports disabled");
                            }
                        })),

                    Case("SkillsDirectoryOverrideHonored", "The configured SkillsDirectory override wins over the default", (CancellationToken ct) =>
                    {
                        string configDir = Path.Combine(Path.GetTempPath(), "mux-skilldiroverride-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(configDir);
                        try
                        {
                            using (SettingsLoader.PushConfigDirectoryOverride(configDir))
                            {
                                string expectedDefault = Path.Combine(configDir, "skills");

                                // No override set: fall back to the default under the config directory.
                                MuxAssert.AreEqual(expectedDefault, SettingsLoader.ResolveSkillsDirectory(new MuxSettings { SkillsDirectory = null }), "null override uses default");
                                MuxAssert.AreEqual(expectedDefault, SettingsLoader.ResolveSkillsDirectory(new MuxSettings { SkillsDirectory = "   " }), "whitespace override uses default");
                                MuxAssert.AreEqual(expectedDefault, SettingsLoader.ResolveSkillsDirectory(null), "null settings uses default");

                                // Override set: it wins verbatim.
                                string custom = Path.Combine(Path.GetTempPath(), "mux-custom-skills-elsewhere");
                                MuxAssert.AreEqual(custom, SettingsLoader.ResolveSkillsDirectory(new MuxSettings { SkillsDirectory = custom }), "explicit override wins");
                            }
                        }
                        finally
                        {
                            try
                            {
                                if (Directory.Exists(configDir))
                                {
                                    Directory.Delete(configDir, true);
                                }
                            }
                            catch (IOException)
                            {
                            }
                        }

                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static void WriteNodeEchoSkill(string root, string id)
        {
            string content = "---\n"
                + "name: " + id + "\n"
                + "description: echoes a marker\n"
                + "mutating: false\n"
                + "commands:\n"
                + "  - name: say\n"
                + "    block: say\n"
                + "    interpreter: node\n"
                + "---\n"
                + "## How to use\n\nRuns a demo.\n\n"
                + "```js id=say\nconsole.log('SKILL_BLOCK_MARKER');\n```\n";

            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
        }

        private static async Task WithSkillsDirAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skillrt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        #endregion
    }
}
