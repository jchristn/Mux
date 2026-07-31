namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Mux.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SkillManager"/> and <see cref="SkillScaffoldWriter"/>: the create,
    /// enable/disable, remove, and import operations that back the manager UI and the CLI verb, tested
    /// directly so their behavior is proven without the modal chain. Config state is isolated through the
    /// AsyncLocal override.
    /// </summary>
    public static class SkillManagerSuite
    {
        private const string SuiteId = "SkillManager";

        /// <summary>
        /// Builds the skill-manager suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the manager cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Skill create, enable/disable, remove, and import",
                new List<TestCaseDescriptor>
                {
                    Case("CreateProducesValidLoadableSkill", "Create writes a scaffold that loads and validates, and enables it", (CancellationToken ct) =>
                        WithConfigDirAsync((skillsDir) =>
                        {
                            SkillManager manager = new SkillManager(skillsDir);
                            SkillScaffold scaffold = new SkillScaffold { Id = "my-skill", Title = "My Skill", Description = "Does a thing.", Mutating = false, Interpreter = "node" };
                            string dir = manager.Create(scaffold);

                            MuxAssert.IsTrue(Directory.Exists(dir), "skill directory created");
                            Skill loaded = new SkillLoader(skillsDir).Load(dir);
                            MuxAssert.IsTrue(loaded.IsValid, "created skill is valid: " + string.Join("; ", loaded.Validation.Errors));

                            List<SkillIndexEntry> index = SettingsLoader.LoadSkillIndex();
                            MuxAssert.IsTrue(index.Exists(e => e.Id == "my-skill" && e.Enabled), "index enabled the new skill");
                            return Task.CompletedTask;
                        })),

                    Case("CreateRejectsBadIdAndCollision", "Create rejects an invalid id and a colliding id", (CancellationToken ct) =>
                        WithConfigDirAsync((skillsDir) =>
                        {
                            SkillManager manager = new SkillManager(skillsDir);
                            MuxAssert.IsTrue(Throws(() => manager.Create(new SkillScaffold { Id = "Bad Id" })), "invalid id rejected");

                            manager.Create(new SkillScaffold { Id = "dup", Description = "x", Interpreter = "node" });
                            MuxAssert.IsTrue(Throws(() => manager.Create(new SkillScaffold { Id = "dup", Interpreter = "node" })), "collision rejected");
                            return Task.CompletedTask;
                        })),

                    Case("SetEnabledPersists", "SetEnabled writes the index and round-trips", (CancellationToken ct) =>
                        WithConfigDirAsync((skillsDir) =>
                        {
                            SkillManager manager = new SkillManager(skillsDir);
                            manager.Create(new SkillScaffold { Id = "toggle-me", Description = "x", Interpreter = "node" });

                            manager.SetEnabled("toggle-me", false);
                            List<SkillIndexEntry> off = SettingsLoader.LoadSkillIndex();
                            MuxAssert.IsTrue(off.Exists(e => e.Id == "toggle-me" && !e.Enabled), "disabled persisted");

                            manager.SetEnabled("toggle-me", true);
                            List<SkillIndexEntry> on = SettingsLoader.LoadSkillIndex();
                            MuxAssert.IsTrue(on.Exists(e => e.Id == "toggle-me" && e.Enabled), "re-enabled persisted");
                            return Task.CompletedTask;
                        })),

                    Case("RemoveDeletesDirectoryAndIndex", "Remove deletes the directory and its index row", (CancellationToken ct) =>
                        WithConfigDirAsync((skillsDir) =>
                        {
                            SkillManager manager = new SkillManager(skillsDir);
                            string dir = manager.Create(new SkillScaffold { Id = "gone", Description = "x", Interpreter = "node" });

                            manager.Remove("gone");
                            MuxAssert.IsFalse(Directory.Exists(dir), "directory removed");
                            MuxAssert.IsFalse(SettingsLoader.LoadSkillIndex().Exists(e => e.Id == "gone"), "index row removed");
                            return Task.CompletedTask;
                        })),

                    Case("ImportCopiesValidSkill", "Import copies a valid skill and rejects invalid or colliding sources", (CancellationToken ct) =>
                        WithConfigDirAsync((skillsDir) =>
                        {
                            SkillManager manager = new SkillManager(skillsDir);

                            string source = Path.Combine(Path.GetTempPath(), "mux-import-src-" + Guid.NewGuid().ToString("N"), "imported-skill");
                            Directory.CreateDirectory(source);
                            File.WriteAllText(Path.Combine(source, "SKILL.md"),
                                "---\nname: imported-skill\ndescription: an imported skill\nmutating: false\ncommands:\n  - name: go\n    block: go\n    interpreter: node\n---\n## How to use\n\nRuns.\n\n```js id=go\nconsole.log('ok');\n```\n");

                            string id = manager.Import(source, null);
                            MuxAssert.AreEqual("imported-skill", id, "imported under its folder id");
                            MuxAssert.IsTrue(Directory.Exists(Path.Combine(skillsDir, "imported-skill")), "copied into the library");
                            MuxAssert.IsTrue(Throws(() => manager.Import(source, null)), "collision rejected");

                            string badSource = Path.Combine(Path.GetTempPath(), "mux-import-bad-" + Guid.NewGuid().ToString("N"), "bad-skill");
                            Directory.CreateDirectory(badSource);
                            File.WriteAllText(Path.Combine(badSource, "SKILL.md"), "---\nname: bad-skill\n---\n");
                            MuxAssert.IsTrue(Throws(() => manager.Import(badSource, null)), "invalid source rejected");

                            try { Directory.Delete(Path.GetDirectoryName(source)!, true); } catch (IOException) { }
                            try { Directory.Delete(Path.GetDirectoryName(badSource)!, true); } catch (IOException) { }
                            return Task.CompletedTask;
                        })),

                    Case("ScaffoldLoadsForEachInterpreter", "The scaffold writer produces a valid skill for each interpreter", (CancellationToken ct) =>
                        WithConfigDirAsync((skillsDir) =>
                        {
                            SkillManager manager = new SkillManager(skillsDir);
                            string[] interpreters = { "pwsh", "bash", "python", "node" };
                            int n = 0;
                            foreach (string interpreter in interpreters)
                            {
                                string id = "scaffold-" + n++;
                                string dir = manager.Create(new SkillScaffold { Id = id, Description = "x", Interpreter = interpreter });
                                Skill loaded = new SkillLoader(skillsDir).Load(dir);
                                MuxAssert.IsTrue(loaded.IsValid, $"scaffold for {interpreter} is valid: " + string.Join("; ", loaded.Validation.Errors));
                            }

                            return Task.CompletedTask;
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task WithConfigDirAsync(Func<string, Task> body)
        {
            string configDir = Path.Combine(Path.GetTempPath(), "mux-skillmgr-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configDir);
            using (SettingsLoader.PushConfigDirectoryOverride(configDir))
            {
                string skillsDir = Path.Combine(configDir, "skills");
                Directory.CreateDirectory(skillsDir);
                try
                {
                    await body(skillsDir).ConfigureAwait(false);
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
            }
        }

        private static bool Throws(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (Exception)
            {
                return true;
            }
        }

        #endregion
    }
}
