namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SkillLoader"/> and <see cref="SkillFrontmatterParser"/>: a well-formed
    /// skill parses and validates, the validation matrix rejects every malformed shape with a clean error,
    /// path-unsafe folders are skipped, and randomized frontmatter never throws out of the parser.
    /// </summary>
    public static class SkillLoaderSuite
    {
        private const string SuiteId = "SkillLoader";

        /// <summary>
        /// Builds the skill-loader suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the loader and parser cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Skill discovery, parsing, and validation",
                new List<TestCaseDescriptor>
                {
                    Case("ValidSkillParsesAndValidates", "A well-formed skill parses, extracts its block, and validates", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string body = "## What this does\n\nRuns a demo.\n\n```bash id=hello\necho hello\n```\n";
                            WriteSkill(root, "demo-skill", ValidFrontmatter("demo-skill") + body);

                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "demo-skill"));

                            MuxAssert.IsTrue(skill.IsValid, "skill is valid: " + string.Join("; ", skill.Validation.Errors));
                            MuxAssert.AreEqual("demo-skill", skill.Manifest.Name, "name parsed");
                            MuxAssert.AreEqual("Demo Skill", skill.Manifest.Title, "title parsed");
                            MuxAssert.IsFalse(skill.Manifest.Mutating, "mutating parsed as false");
                            MuxAssert.AreEqual(2, skill.Manifest.Tags.Count, "two tags parsed");
                            MuxAssert.AreEqual(1, skill.Manifest.Commands.Count, "one command parsed");
                            MuxAssert.AreEqual("hello", skill.Manifest.Commands[0].BlockId, "command block id parsed");
                            MuxAssert.IsTrue(skill.CodeBlocks.ContainsKey("hello"), "code block extracted");
                            MuxAssert.Contains("echo hello", skill.CodeBlocks["hello"], "block content captured");
                            return Task.CompletedTask;
                        })),

                    Case("MissingSkillMdIsInvalid", "A folder without SKILL.md is reported invalid", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            Directory.CreateDirectory(Path.Combine(root, "empty-skill"));
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "empty-skill"));
                            MuxAssert.IsFalse(skill.IsValid, "missing SKILL.md invalid");
                            return Task.CompletedTask;
                        })),

                    Case("NameMismatchIsInvalid", "A name that does not match the folder is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            WriteSkill(root, "folder-a", ValidFrontmatter("other-name"));
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "folder-a"));
                            MuxAssert.IsFalse(skill.IsValid, "name mismatch invalid");
                            return Task.CompletedTask;
                        })),

                    Case("MissingDescriptionIsInvalid", "A skill without a description is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: no-desc\ntitle: No Desc\nmutating: false\n---\n";
                            WriteSkill(root, "no-desc", fm);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "no-desc"));
                            MuxAssert.IsFalse(skill.IsValid, "missing description invalid");
                            return Task.CompletedTask;
                        })),

                    Case("BadInterpreterIsInvalid", "A command with a disallowed interpreter is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: bad-interp\ndescription: x\ncommands:\n  - name: go\n    block: b\n    interpreter: ruby\n---\n";
                            string body = "```ruby id=b\nputs 1\n```\n";
                            WriteSkill(root, "bad-interp", fm + body);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "bad-interp"));
                            MuxAssert.IsFalse(skill.IsValid, "disallowed interpreter invalid");
                            return Task.CompletedTask;
                        })),

                    Case("BothRunAndBlockIsInvalid", "A command that sets both run and block is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: both\ndescription: x\ncommands:\n  - name: go\n    run: scripts/a.sh\n    block: b\n    interpreter: bash\n---\n";
                            string body = "```bash id=b\ntrue\n```\n";
                            WriteSkill(root, "both", fm + body);
                            Directory.CreateDirectory(Path.Combine(root, "both", "scripts"));
                            File.WriteAllText(Path.Combine(root, "both", "scripts", "a.sh"), "true");
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "both"));
                            MuxAssert.IsFalse(skill.IsValid, "both run and block invalid");
                            return Task.CompletedTask;
                        })),

                    Case("NeitherRunNorBlockIsInvalid", "A command that sets neither run nor block is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: neither\ndescription: x\ncommands:\n  - name: go\n    interpreter: bash\n---\n";
                            WriteSkill(root, "neither", fm);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "neither"));
                            MuxAssert.IsFalse(skill.IsValid, "neither run nor block invalid");
                            return Task.CompletedTask;
                        })),

                    Case("MissingBlockReferenceIsInvalid", "A command pointing at a nonexistent block is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: ghost-block\ndescription: x\ncommands:\n  - name: go\n    block: nope\n    interpreter: bash\n---\n";
                            WriteSkill(root, "ghost-block", fm);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "ghost-block"));
                            MuxAssert.IsFalse(skill.IsValid, "missing block reference invalid");
                            return Task.CompletedTask;
                        })),

                    Case("ScriptPathEscapeIsInvalid", "A command whose script escapes the directory is rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: escaper\ndescription: x\ncommands:\n  - name: go\n    run: ../outside.sh\n    interpreter: bash\n---\n";
                            WriteSkill(root, "escaper", fm);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "escaper"));
                            MuxAssert.IsFalse(skill.IsValid, "script path escape invalid");
                            return Task.CompletedTask;
                        })),

                    Case("DuplicateCommandNameIsInvalid", "Two commands with the same name are rejected", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            string fm = "---\nname: dup\ndescription: x\ncommands:\n  - name: go\n    block: b\n    interpreter: bash\n  - name: go\n    block: b\n    interpreter: bash\n---\n";
                            string body = "```bash id=b\ntrue\n```\n";
                            WriteSkill(root, "dup", fm + body);
                            Skill skill = new SkillLoader(root).Load(Path.Combine(root, "dup"));
                            MuxAssert.IsFalse(skill.IsValid, "duplicate command name invalid");
                            return Task.CompletedTask;
                        })),

                    Case("DiscoverSkipsUnsafeFolders", "Discovery skips path-unsafe folder names", (CancellationToken ct) =>
                        WithSkillsDirAsync((root) =>
                        {
                            WriteSkill(root, "good-one", ValidFrontmatter("good-one"));
                            Directory.CreateDirectory(Path.Combine(root, "has space"));
                            File.WriteAllText(Path.Combine(root, "has space", "SKILL.md"), ValidFrontmatter("has space"));

                            IReadOnlyList<Skill> skills = new SkillLoader(root).Discover();
                            MuxAssert.AreEqual(1, skills.Count, "only the safe folder is discovered");
                            MuxAssert.AreEqual("good-one", skills[0].Manifest.Name, "safe skill loaded");
                            return Task.CompletedTask;
                        })),

                    Case("FrontmatterFuzzDoesNotThrow", "Randomized frontmatter never throws out of the parser", (CancellationToken ct) =>
                    {
                        Random random = new Random(20260731);
                        string[] fragments =
                        {
                            "name: x", "title:", "description: \"q\"", "mutating: maybe", "tags: [a, b,",
                            "commands:", "  - name: go", "    block:", "    run: ../x", "  weird", "::::",
                            "- top", "   ", "\t\tname: y", "enabled: yes # comment", "[]{}", "\"unterminated",
                            "'also", "commands: [inline]", "  - ", "timeoutMs: notanumber"
                        };

                        for (int i = 0; i < 500; i++)
                        {
                            StringBuilder builder = new StringBuilder();
                            int count = random.Next(0, 12);
                            for (int j = 0; j < count; j++)
                            {
                                builder.Append(fragments[random.Next(fragments.Length)]);
                                builder.Append('\n');
                            }

                            SkillManifest manifest = SkillFrontmatterParser.Parse(builder.ToString());
                            MuxAssert.IsNotNull(manifest, "parser returns a manifest for input " + i);
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

        private static async Task WithSkillsDirAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-skills-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                TryDelete(root);
            }
        }

        private static void WriteSkill(string root, string id, string content)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
        }

        private static string ValidFrontmatter(string name)
        {
            return "---\n"
                + "name: " + name + "\n"
                + "title: Demo Skill\n"
                + "description: A demo skill for tests.\n"
                + "version: 1.0.0\n"
                + "mutating: false\n"
                + "whenToUse: When a test needs a valid skill.\n"
                + "tags: [demo, test]\n"
                + "commands:\n"
                + "  - name: hello\n"
                + "    description: Say hello.\n"
                + "    block: hello\n"
                + "    interpreter: bash\n"
                + "---\n";
        }

        private static void TryDelete(string dir)
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
        }

        #endregion
    }
}
