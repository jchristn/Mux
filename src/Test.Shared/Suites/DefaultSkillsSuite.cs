namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the seeded default skill library: every default validates, seeding preserves
    /// user edits and does not duplicate, and a representative default actually runs.
    /// </summary>
    public static class DefaultSkillsSuite
    {
        private const string SuiteId = "DefaultSkills";

        /// <summary>
        /// Builds the default-skills suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the default-library cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "The seeded default skill library",
                new List<TestCaseDescriptor>
                {
                    Case("AllDefaultsValidate", "Every seeded default skill parses and validates", (CancellationToken ct) =>
                        WithDirAsync((root) =>
                        {
                            DefaultSkillLibrary.SeedInto(root);
                            IReadOnlyList<Skill> skills = new SkillLoader(root).Discover();

                            MuxAssert.AreEqual(DefaultSkillLibrary.All().Count, skills.Count, "all defaults discovered");
                            foreach (Skill skill in skills)
                            {
                                MuxAssert.IsTrue(skill.IsValid, $"{skill.Manifest.Name} valid: " + string.Join("; ", skill.Validation.Errors));
                            }

                            return Task.CompletedTask;
                        })),

                    Case("SeedPreservesEditsAndDoesNotDuplicate", "Re-seeding leaves edited skills untouched and adds nothing new", (CancellationToken ct) =>
                        WithDirAsync((root) =>
                        {
                            DefaultSkillLibrary.SeedInto(root);
                            string marker = Path.Combine(root, "env-report", "SKILL.md");
                            File.AppendAllText(marker, "\n<!-- user edit -->\n");
                            int before = Directory.GetDirectories(root).Length;

                            DefaultSkillLibrary.SeedInto(root);
                            int after = Directory.GetDirectories(root).Length;

                            MuxAssert.AreEqual(before, after, "no duplicate directories");
                            MuxAssert.Contains("user edit", File.ReadAllText(marker), "user edit preserved");
                            return Task.CompletedTask;
                        })),

                    Case("SeedNewIntoUpgradesExistingLibrary", "SeedNewInto adds newly shipped defaults to a pre-existing library", (CancellationToken ct) =>
                        WithDirAsync((root) =>
                        {
                            // Simulate an older install that already has one default and no manifest.
                            Directory.CreateDirectory(Path.Combine(root, "env-report"));
                            File.WriteAllText(Path.Combine(root, "env-report", "SKILL.md"), "---\nname: env-report\n---\n");

                            IReadOnlyList<string> added = DefaultSkillLibrary.SeedNewInto(root);

                            int expected = DefaultSkillLibrary.All().Count;
                            IReadOnlyList<Skill> skills = new SkillLoader(root).Discover();
                            MuxAssert.AreEqual(expected, skills.Count, "the full catalog is present after upgrade");
                            // Everything except the pre-existing env-report was written this run.
                            MuxAssert.AreEqual(expected - 1, added.Count, "every missing default was added");
                            MuxAssert.IsFalse(added.Contains("env-report"), "the pre-existing skill was not rewritten");
                            return Task.CompletedTask;
                        })),

                    Case("SeedNewIntoDoesNotResurrectDeleted", "SeedNewInto does not re-create a default the user has deleted", (CancellationToken ct) =>
                        WithDirAsync((root) =>
                        {
                            DefaultSkillLibrary.SeedNewInto(root);
                            int full = Directory.GetDirectories(root).Length;

                            // The user removes a default; the manifest still records it as seeded.
                            Directory.Delete(Path.Combine(root, "env-report"), true);

                            IReadOnlyList<string> added = DefaultSkillLibrary.SeedNewInto(root);

                            MuxAssert.AreEqual(0, added.Count, "nothing is added on a steady-state re-seed");
                            MuxAssert.IsFalse(Directory.Exists(Path.Combine(root, "env-report")), "the deleted default stays deleted");
                            MuxAssert.AreEqual(full - 1, Directory.GetDirectories(root).Length, "no directory was resurrected");
                            return Task.CompletedTask;
                        })),

                    Case("NodeDefaultRuns", "The json-validate default runs and reports valid and invalid JSON", (CancellationToken ct) =>
                        WithDirAsync(async (root) =>
                        {
                            DefaultSkillLibrary.SeedInto(root);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "json-validate"));
                            MuxAssert.IsTrue(skill.IsValid, "json-validate valid");
                            SkillCommand command = skill.Manifest.Commands[0];

                            string good = Path.Combine(root, "good.json");
                            File.WriteAllText(good, "{\"a\":1}");
                            ToolResult ok = await new SkillExecutor().ExecuteAsync("t", skill, command, new List<string> { good }, root, ct).ConfigureAwait(false);
                            MuxAssert.IsTrue(ok.Success, "valid JSON passes: " + ok.Content);

                            string bad = Path.Combine(root, "bad.json");
                            File.WriteAllText(bad, "{ not json ");
                            ToolResult fail = await new SkillExecutor().ExecuteAsync("t", skill, command, new List<string> { bad }, root, ct).ConfigureAwait(false);
                            MuxAssert.IsFalse(fail.Success, "invalid JSON fails");
                        }))
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static async Task WithDirAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-defaults-" + Guid.NewGuid().ToString("N"));
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
