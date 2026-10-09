namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Skills;
    using Mux.Core.Skills.Packaging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the skill content mux ships (src/Mux.Core/Skills/Bundled and src/Mux.Core/Skills/Packs):
    /// counts, validity, categories, unique ids, no em-dashes, every <c>${SKILL_DIR}</c> reference resolving, no bare
    /// references to bundled files in prose, no Claude-only instructions, well-formed packs, and the embedded resources
    /// matching the source tree. Each check helper also has negative fixtures. Content checks are skipped when the
    /// source tree is not next to the test build.
    /// </summary>
    public static class ShippedSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "ShippedSkills";

        private static readonly Regex _Frontmatter = new Regex(@"\A---\r?\n.*?\r?\n---\r?\n", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? src = ImportedSkillChecks.FindSourceRoot();
            string skillsRoot = src == null ? string.Empty : Path.Combine(src, "Mux.Core", "Skills");
            bool haveTree = src != null;
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Action body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => { body(); return Task.CompletedTask; }, skip: !haveTree, skipReason: "the source tree is not next to the test build"));
            }

            void AddFixture(string id, string name, Action<string> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) =>
                {
                    string dir = Path.Combine(Path.GetTempPath(), "mux-shipped-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    try { body(dir); } finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
                    return Task.CompletedTask;
                }));
            }

            Add("CountsMatchThePlan", "17 bundled defaults and 166 pack skills in 10 packs ship", () =>
            {
                MuxAssert.AreEqual(17, SkillFolders(Path.Combine(skillsRoot, "Bundled")).Count, "bundled defaults");
                List<string> packs = PackFolders(skillsRoot);
                MuxAssert.AreEqual(10, packs.Count, "packs");
                int packSkills = 0;
                foreach (string pack in packs) packSkills += SkillFolders(pack).Count;
                MuxAssert.AreEqual(166, packSkills, "pack skills");
            });
            Add("EverySkillValidates", "Every shipped skill loads with no errors and no warnings", () =>
            {
                foreach (string folder in AllSkillFolders(skillsRoot))
                {
                    Skill skill = new SkillLoader(Path.GetDirectoryName(folder)!).Load(folder);
                    MuxAssert.IsTrue(skill.IsValid, Rel(skillsRoot, folder) + " valid: " + string.Join("; ", skill.Validation.Errors));
                    MuxAssert.AreEqual(0, skill.Validation.Warnings.Count, Rel(skillsRoot, folder) + " has no warnings: " + string.Join("; ", skill.Validation.Warnings));
                }
            });
            Add("CategoriesAndProvenance", "Every shipped skill has a known category, a source, and the MIT license", () =>
            {
                foreach (string folder in AllSkillFolders(skillsRoot))
                {
                    string text = File.ReadAllText(Path.Combine(folder, "SKILL.md"));
                    string? category = SkillImportNormalizer.ReadFrontmatterValue(text, "category");
                    MuxAssert.IsTrue(SkillCategories.IsKnown(category), Rel(skillsRoot, folder) + " category '" + category + "' is canonical");
                    MuxAssert.IsTrue(!string.IsNullOrWhiteSpace(SkillImportNormalizer.ReadFrontmatterValue(text, "source")), Rel(skillsRoot, folder) + " has a source");
                    MuxAssert.AreEqual("MIT", SkillImportNormalizer.ReadFrontmatterValue(text, "license"), Rel(skillsRoot, folder) + " license");
                }
            });
            Add("IdsAreUnique", "Skill ids are unique across the bundled defaults, the packs, and the C#-defined defaults", () =>
            {
                Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DefaultSkillDef definition in DefaultSkillLibrary.Definitions()) seen[definition.Id] = "C# default";
                foreach (string id in new[] { "git-status-vs-head", "new-tool", "new-class" }) MuxAssert.IsTrue(DefaultSkillLibrary.All().ContainsKey(id), "legacy default " + id + " present");
                foreach (string folder in AllSkillFolders(skillsRoot))
                {
                    string id = Path.GetFileName(folder);
                    if (seen.TryGetValue(id, out string? where) && where != "bundled-self")
                    {
                        MuxAssert.Fail(id + " in " + Rel(skillsRoot, folder) + " collides with " + where);
                    }

                    seen[id] = Rel(skillsRoot, folder);
                }
            });
            Add("NoEmDashes", "No shipped skill file contains an em-dash", () =>
            {
                List<string> found = new List<string>();
                foreach (string folder in AllSkillFolders(skillsRoot)) found.AddRange(ImportedSkillChecks.FilesWithEmDash(folder).ConvertAll(f => Rel(skillsRoot, folder) + "/" + f));
                MuxAssert.AreEqual(0, found.Count, "files with em-dashes: " + string.Join(", ", found));
            });
            Add("SkillDirReferencesResolve", "Every ${SKILL_DIR}/... reference in every shipped file points at a file or folder that ships with the skill", () =>
            {
                List<string> missing = new List<string>();
                foreach (string folder in AllSkillFolders(skillsRoot))
                {
                    foreach (string file in TextFiles(folder))
                    {
                        foreach (string target in ImportedSkillChecks.MissingPlaceholderTargets(folder, File.ReadAllText(file)))
                        {
                            missing.Add(Rel(skillsRoot, file) + " -> " + target);
                        }
                    }
                }

                MuxAssert.AreEqual(0, missing.Count, "unresolved references: " + string.Join("; ", missing));
            });
            Add("NoBareBundledPathsInProse", "Skill instructions refer to bundled files through ${SKILL_DIR}; frontmatter run: lines and command tables are exempt", () =>
            {
                List<string> bare = new List<string>();
                foreach (string folder in AllSkillFolders(skillsRoot))
                {
                    string prose = Prose(File.ReadAllText(Path.Combine(folder, "SKILL.md")));
                    foreach (string path in ImportedSkillChecks.BareBundledPaths(folder, prose))
                    {
                        bare.Add(Rel(skillsRoot, folder) + ": " + path);
                    }
                }

                MuxAssert.AreEqual(0, bare.Count, "bare bundled paths: " + string.Join("; ", bare));
            });
            Add("NoClaudeOnlyInstructions", "No shipped SKILL.md tells the model to use CLAUDE.md without naming mux's instruction files", () =>
            {
                List<string> lines = new List<string>();
                foreach (string folder in AllSkillFolders(skillsRoot))
                {
                    foreach (string line in ImportedSkillChecks.ClaudeOnlyLines(File.ReadAllText(Path.Combine(folder, "SKILL.md")))) lines.Add(Rel(skillsRoot, folder) + ": " + line.Trim());
                }

                MuxAssert.AreEqual(0, lines.Count, "Claude-only lines: " + string.Join(" | ", lines));
            });
            Add("PacksAreWellFormed", "Every pack folder has a well-formed pack.json matching its folder", () =>
            {
                foreach (string pack in PackFolders(skillsRoot))
                {
                    List<string> problems = ImportedSkillChecks.PackProblems(pack);
                    MuxAssert.AreEqual(0, problems.Count, Path.GetFileName(pack) + ": " + string.Join("; ", problems));
                }
            });
            Add("EmbeddedMatchesSource", "The embedded bundled skills and packs match the source tree", () =>
            {
                Dictionary<string, int> fromSource = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string pack in PackFolders(skillsRoot)) fromSource[Path.GetFileName(pack)] = SkillFolders(pack).Count;
                MuxAssert.AreEqual(fromSource.Count, SkillPackCatalog.Embedded.Packs.Count, "embedded pack count");
                foreach (SkillPack pack in SkillPackCatalog.Embedded.Packs)
                {
                    MuxAssert.IsTrue(fromSource.ContainsKey(pack.Id), pack.Id + " exists in the source tree");
                    MuxAssert.AreEqual(fromSource[pack.Id], pack.Skills.Count, pack.Id + " skill count");
                }

                foreach (string folder in SkillFolders(Path.Combine(skillsRoot, "Bundled")))
                {
                    MuxAssert.IsTrue(DefaultSkillLibrary.All().ContainsKey(Path.GetFileName(folder)), Path.GetFileName(folder) + " is a seeded default");
                }
            });

            // --- negative fixtures for each check ---
            AddFixture("ChecksCatchEmDashes", "The em-dash check flags a text file and ignores binary files", (string dir) =>
            {
                File.WriteAllText(Path.Combine(dir, "a.md"), "one " + ImportedSkillChecks.EmDash + " two");
                File.WriteAllText(Path.Combine(dir, "b.md"), "clean, text");
                File.WriteAllBytes(Path.Combine(dir, "c.bin"), new byte[] { 0, 0xE2, 0x80, 0x94 });
                List<string> found = ImportedSkillChecks.FilesWithEmDash(dir);
                MuxAssert.AreEqual(1, found.Count, "one file flagged");
                MuxAssert.AreEqual("a.md", found[0], "the text file");
            });
            AddFixture("ChecksCatchMissingTargets", "The placeholder check flags references to files that do not exist", (string dir) =>
            {
                Directory.CreateDirectory(Path.Combine(dir, "scripts"));
                File.WriteAllText(Path.Combine(dir, "scripts", "real.py"), "x");
                List<string> missing = ImportedSkillChecks.MissingPlaceholderTargets(dir, "run `python3 \"${SKILL_DIR}/scripts/real.py\"` then ${SKILL_DIR}/scripts/gone.py.");
                MuxAssert.AreEqual(1, missing.Count, "one missing");
                MuxAssert.AreEqual("scripts/gone.py", missing[0], "the missing file");
                MuxAssert.AreEqual(0, ImportedSkillChecks.MissingPlaceholderTargets(dir, "no references").Count, "nothing to check");
            });
            AddFixture("ChecksCatchBarePaths", "The bare-path check flags prose references to real bundled files only", (string dir) =>
            {
                Directory.CreateDirectory(Path.Combine(dir, "references"));
                File.WriteAllText(Path.Combine(dir, "references", "guide.md"), "x");
                List<string> bare = ImportedSkillChecks.BareBundledPaths(dir, "See references/guide.md and src/references/other.md and ${SKILL_DIR}/references/guide.md.");
                MuxAssert.AreEqual(1, bare.Count, "only the bare real path: " + string.Join(",", bare));
                MuxAssert.AreEqual(0, ImportedSkillChecks.BareBundledPaths(dir, Prose("---\nname: x\ncommands:\n  - name: a\n    run: references/guide.md\n---\n| `a` | references/guide.md |\n")).Count, "frontmatter and tables are exempt");
            });
            AddFixture("ChecksCatchClaudeOnlyLines", "The Claude reference check flags CLAUDE.md without mux's instruction files", (string dir) =>
            {
                List<string> lines = ImportedSkillChecks.ClaudeOnlyLines("Read CLAUDE.md first.\nRead the project instruction file (MUX.md, AGENTS.md, or CLAUDE.md).\n");
                MuxAssert.AreEqual(1, lines.Count, "one line flagged");
                MuxAssert.Contains("Read CLAUDE.md first.", lines[0], "the Claude-only line");
            });
            AddFixture("ChecksCatchBadPacks", "The pack check flags a missing file, bad JSON, missing keys, a mismatched id, and a non-MIT license", (string dir) =>
            {
                string missing = Path.Combine(dir, "missing");
                Directory.CreateDirectory(missing);
                MuxAssert.Contains("pack.json is missing", string.Join(";", ImportedSkillChecks.PackProblems(missing)), "missing file");
                string bad = Path.Combine(dir, "bad");
                Directory.CreateDirectory(bad);
                File.WriteAllText(Path.Combine(bad, "pack.json"), "{not json");
                MuxAssert.Contains("not valid JSON", string.Join(";", ImportedSkillChecks.PackProblems(bad)), "bad JSON");
                string wrong = Path.Combine(dir, "wrong");
                Directory.CreateDirectory(wrong);
                File.WriteAllText(Path.Combine(wrong, "pack.json"), "{\"id\":\"other\",\"title\":\"T\",\"description\":\"D\",\"category\":\"data\",\"source\":\"s\",\"license\":\"GPL\"}");
                string problems = string.Join(";", ImportedSkillChecks.PackProblems(wrong));
                MuxAssert.Contains("does not match folder", problems, "id mismatch");
                MuxAssert.Contains("license is not MIT", problems, "license");
                string partial = Path.Combine(dir, "partial");
                Directory.CreateDirectory(partial);
                File.WriteAllText(Path.Combine(partial, "pack.json"), "{\"id\":\"partial\"}");
                MuxAssert.Contains("missing 'title'", string.Join(";", ImportedSkillChecks.PackProblems(partial)), "missing keys");
            });

            return new TestSuiteDescriptor(SuiteId, "Shipped skill content: counts, validity, categories, paths, packs", cases);
        }

        #endregion

        #region Private-Methods

        private static List<string> SkillFolders(string root)
        {
            List<string> folders = new List<string>();
            if (!Directory.Exists(root)) return folders;
            foreach (string folder in Directory.GetDirectories(root))
            {
                if (File.Exists(Path.Combine(folder, "SKILL.md"))) folders.Add(folder);
            }

            folders.Sort(StringComparer.Ordinal);
            return folders;
        }

        private static List<string> PackFolders(string skillsRoot)
        {
            List<string> packs = new List<string>();
            string root = Path.Combine(skillsRoot, "Packs");
            if (!Directory.Exists(root)) return packs;
            foreach (string folder in Directory.GetDirectories(root)) packs.Add(folder);
            packs.Sort(StringComparer.Ordinal);
            return packs;
        }

        private static List<string> AllSkillFolders(string skillsRoot)
        {
            List<string> all = SkillFolders(Path.Combine(skillsRoot, "Bundled"));
            foreach (string pack in PackFolders(skillsRoot)) all.AddRange(SkillFolders(pack));
            return all;
        }

        private static IEnumerable<string> TextFiles(string folder)
        {
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                string extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension == ".md" || extension == ".py" || extension == ".sh" || extension == ".js" || extension == ".ts" || extension == ".txt" || extension == ".json" || extension == ".yaml" || extension == ".yml")
                {
                    yield return file;
                }
            }
        }

        // The SKILL.md body without its frontmatter (where run: paths are relative to the skill folder) and without
        // table rows (the Commands tables name each command's script by its relative path).
        private static string Prose(string text)
        {
            string body = _Frontmatter.Replace(text ?? string.Empty, string.Empty, 1);
            List<string> kept = new List<string>();
            foreach (string line in body.Replace("\r\n", "\n").Split('\n'))
            {
                if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        private static string Rel(string root, string path)
        {
            return Path.GetRelativePath(root, path).Replace('\\', '/');
        }

        #endregion
    }
}
