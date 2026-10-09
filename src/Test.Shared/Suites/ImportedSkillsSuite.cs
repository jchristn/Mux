namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Skills;
    using Mux.Core.Skills.Packaging;
    using Mux.Server;
    using Mux.Server.Routes;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for the skill infrastructure behind SKILLS_TO_CONSIDER.md Phase 0: folder placeholders
    /// (<c>${SKILL_DIR}</c> and its aliases) in the <c>skill</c> tool, invocation expansion, and <c>mux skill show</c>;
    /// the bundled-file listing; the environment a skill command sees; folder-based bundled defaults (seeding, upgrade
    /// top-up, edits and deletions respected, executable scripts); skill packs (catalog, install, remove, the manifest
    /// that protects user skills); the importer's normalization rules and end-to-end imports (local and git); and the
    /// CLI, REST, terminal, and dashboard surfaces. Positive and negative cases throughout.
    /// </summary>
    public static class ImportedSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "ImportedSkills";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the imported-skills cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string dir) => body(dir, ct))));
            }

            void AddSync(string id, string name, Action<string> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string dir) => { body(dir); return Task.CompletedTask; })));
            }

            bool pwsh = IsOnPath("pwsh");
            bool git = IsOnPath("git");

            // --- placeholders and file listing ---
            AddSync("SubstituteEveryAlias", "Every folder placeholder resolves to the skill folder; look-alike variables do not", (string dir) =>
            {
                string folder = SkillPathResolver.NormalizeFolder(dir);
                string text = "a ${SKILL_DIR} b ${MUX_SKILL_DIR} c ${CLAUDE_SKILL_DIR} d ${CLAUDE_PLUGIN_ROOT} e ${SKILL_ROOT} f {baseDir} g {skill_path} h {skillDir} i $SKILL_DIR/x j $SKILL_ROOT k $CLAUDE_SKILL_DIR";
                string resolved = SkillPathResolver.Substitute(text, dir);
                MuxAssert.AreEqual(11, Regex.Matches(resolved, Regex.Escape(folder)).Count, "eleven placeholders resolved: " + resolved);
                MuxAssert.IsFalse(SkillPathResolver.ContainsPlaceholder(resolved), "nothing left");
                MuxAssert.AreEqual("$SKILL_DIRECTORY and $SKILL_DIR_X", SkillPathResolver.Substitute("$SKILL_DIRECTORY and $SKILL_DIR_X", dir), "longer names untouched");
                MuxAssert.AreEqual("keep ${SKILL_DIR}", SkillPathResolver.Substitute("keep ${SKILL_DIR}", "  "), "blank folder leaves text");
                MuxAssert.AreEqual(string.Empty, SkillPathResolver.Substitute(null, dir), "null text");
                MuxAssert.IsTrue(SkillPathResolver.ContainsPlaceholder("x {baseDir}"), "detects brace form");
                MuxAssert.IsFalse(SkillPathResolver.ContainsPlaceholder("plain"), "plain text");
                MuxAssert.IsFalse(folder.EndsWith("/", StringComparison.Ordinal) || folder.Contains('\\', StringComparison.Ordinal), "forward slashes, no trailing slash");
            });
            AddSync("ListFilesBoundsAndExclusions", "The file listing skips SKILL.md, dot entries, and dependency folders, and stops at its limits", (string dir) =>
            {
                Write(dir, "SKILL.md", "---\nname: x\ndescription: d\n---\n");
                Write(dir, "scripts/run.py", "x");
                Write(dir, "references/guide.md", "x");
                Write(dir, ".secret", "x");
                Write(dir, ".git/HEAD", "x");
                Write(dir, "node_modules/lib/index.js", "x");
                Write(dir, "scripts/__pycache__/run.pyc", "x");
                Write(dir, "a/b/c/d/e/deep.txt", "x");
                Write(dir, "nested/SKILL.md", "x");
                List<string> files = SkillPathResolver.ListFiles(dir, out bool truncated, 200, 4);
                MuxAssert.AreEqual("nested/SKILL.md,references/guide.md,scripts/run.py", string.Join(",", files), "expected files only: " + string.Join(",", files));
                MuxAssert.IsTrue(truncated, "the deep folder past depth 4 marks the list truncated");
                List<string> limited = SkillPathResolver.ListFiles(dir, out bool cut, 2, 4);
                MuxAssert.AreEqual(2, limited.Count, "file limit");
                MuxAssert.IsTrue(cut, "truncated by count");
                MuxAssert.AreEqual(0, SkillPathResolver.ListFiles(Path.Combine(dir, "missing"), out bool none).Count, "missing folder");
                MuxAssert.IsFalse(none, "not truncated");
                MuxAssert.AreEqual(0, SkillPathResolver.ListFiles(null, out _).Count, "null folder");
            });
            Add("SkillToolShowsFolderFilesAndResolvedBody", "The skill tool returns the folder, the bundled files, and a body with placeholders resolved", async (string dir, CancellationToken ct) =>
            {
                string skillDir = Path.Combine(dir, "auditor");
                Write(skillDir, "SKILL.md", "---\nname: auditor\ndescription: Audit things.\n---\n\nRun `python \"${SKILL_DIR}/scripts/audit.py\"` and read {baseDir}/references/guide.md.\n");
                Write(skillDir, "scripts/audit.py", "print(1)");
                Write(skillDir, "references/guide.md", "guide");
                SkillToolProvider provider = new SkillToolProvider(new SkillCatalog(new SkillLoader(dir).Discover()), new SkillExecutor());
                using (JsonDocument args = JsonDocument.Parse("{\"name\":\"auditor\"}"))
                {
                    ToolResult result = await provider.ExecuteAsync("skill", args.RootElement, dir, ct).ConfigureAwait(false);
                    MuxAssert.IsTrue(result.Success, "opened: " + result.Content);
                    using (JsonDocument doc = JsonDocument.Parse(result.Content))
                    {
                        string folder = SkillPathResolver.NormalizeFolder(skillDir);
                        MuxAssert.AreEqual(folder, doc.RootElement.GetProperty("directory").GetString(), "directory");
                        string files = doc.RootElement.GetProperty("files").ToString();
                        MuxAssert.Contains("scripts/audit.py", files, "script listed");
                        MuxAssert.Contains("references/guide.md", files, "reference listed");
                        MuxAssert.DoesNotContain("SKILL.md", files, "SKILL.md not listed");
                        string body = doc.RootElement.GetProperty("body").GetString() ?? string.Empty;
                        MuxAssert.Contains(folder + "/scripts/audit.py", body, "${SKILL_DIR} resolved");
                        MuxAssert.Contains(folder + "/references/guide.md", body, "{baseDir} resolved");
                        MuxAssert.DoesNotContain("${SKILL_DIR}", body, "no placeholder left");
                        MuxAssert.Contains("relative to directory", doc.RootElement.GetProperty("paths_note").GetString() ?? string.Empty, "paths note");
                    }
                }

                using (JsonDocument missing = JsonDocument.Parse("{\"name\":\"nope\"}"))
                {
                    ToolResult notFound = await provider.ExecuteAsync("skill", missing.RootElement, dir, ct).ConfigureAwait(false);
                    MuxAssert.IsFalse(notFound.Success, "unknown skill refused");
                }
            });
            AddSync("InvocationResolvesAndNamesFolder", "/skill invocation resolves placeholders and names the folder only for skills with bundled files", (string dir) =>
            {
                string bundled = Path.Combine(dir, "with-files");
                Write(bundled, "SKILL.md", "---\nname: with-files\ndescription: d\n---\n\nUse ${CLAUDE_SKILL_DIR}/scripts/a.sh on $ARGUMENTS.\n");
                Write(bundled, "scripts/a.sh", "echo");
                string helperOnly = Path.Combine(dir, "helper-only");
                Write(helperOnly, "SKILL.md", "---\nname: helper-only\ndescription: d\n---\n\nPlain body.\n");
                Write(helperOnly, "resources/mux-skill.ps1", "x");
                SkillLoader loader = new SkillLoader(dir);
                SkillInvocation invocation = SkillInvocationExpander.Expand(loader.Load(bundled), "src");
                string folder = SkillPathResolver.NormalizeFolder(bundled);
                MuxAssert.Contains("Use " + folder + "/scripts/a.sh on src.", invocation.Prompt, "placeholder and $ARGUMENTS resolved");
                MuxAssert.Contains("This skill's files are in " + folder, invocation.Prompt, "folder named");
                SkillInvocation plain = SkillInvocationExpander.Expand(loader.Load(helperOnly), string.Empty);
                MuxAssert.DoesNotContain("This skill's files are in", plain.Prompt, "no folder note for the helper alone");
            });
            cases.Add(new TestCaseDescriptor(SuiteId, "CommandEnvironmentHasFolderVariables", "A skill command sees MUX_SKILL_DIR, SKILL_DIR, and CLAUDE_SKILL_DIR", (CancellationToken ct) => WithTempAsync(async (string dir) =>
            {
                string skillDir = Path.Combine(dir, "env-check");
                Write(skillDir, "SKILL.md", "---\nname: env-check\ndescription: d\ncommands:\n  - name: show\n    description: print\n    block: show\n    interpreter: pwsh\n---\n\n```pwsh id=show\nWrite-Output (\"A=\" + $env:MUX_SKILL_DIR)\nWrite-Output (\"B=\" + $env:SKILL_DIR)\nWrite-Output (\"C=\" + $env:CLAUDE_SKILL_DIR)\n```\n");
                Skill skill = new SkillLoader(dir).Load(skillDir);
                MuxAssert.IsTrue(skill.IsValid, "valid: " + string.Join("; ", skill.Validation.Errors));
                ToolResult result = await new SkillExecutor().ExecuteAsync("t", skill, skill.Manifest.Commands[0], new List<string>(), dir, ct).ConfigureAwait(false);
                foreach (string letter in new[] { "A", "B", "C" })
                {
                    MuxAssert.Contains(letter + "=" + skillDir.Replace("\\", "\\\\"), result.Content, letter + " set: " + result.Content);
                }
            }), skip: !pwsh, skipReason: "pwsh is not on PATH"));
            AddSync("SkillShowResolvesBody", "mux skill show prints the folder, the files, and the body with placeholders resolved", (string dir) =>
            {
                string config = Path.Combine(dir, "cfg");
                string skills = Path.Combine(config, "skills");
                Directory.CreateDirectory(config);
                File.WriteAllText(Path.Combine(config, "settings.json"), "{\"skillsDirectory\":" + JsonSerializer.Serialize(skills) + "}");
                Write(Path.Combine(skills, "shown"), "SKILL.md", "---\nname: shown\ndescription: d\nsource: https://example.com/r\nlicense: MIT\n---\n\nRun ${SKILL_DIR}/scripts/go.sh now.\n");
                Write(Path.Combine(skills, "shown"), "scripts/go.sh", "echo");
                CliInvocationResult text = InvokeCli(new[] { "skill", "show", "shown", "--config-dir", config });
                string folder = SkillPathResolver.NormalizeFolder(Path.Combine(skills, "shown"));
                MuxAssert.Contains("Directory:   " + folder, text.StdOut, "directory line: " + text.StdOut + text.StdErr);
                MuxAssert.Contains("Files:       scripts/go.sh", text.StdOut, "files line");
                MuxAssert.Contains("Source:      https://example.com/r", text.StdOut, "source line");
                MuxAssert.Contains("License:     MIT", text.StdOut, "license line");
                MuxAssert.Contains("Run " + folder + "/scripts/go.sh now.", text.StdOut, "body resolved");
                CliInvocationResult json = InvokeCli(new[] { "skill", "show", "shown", "--config-dir", config, "--output-format", "json" });
                MuxAssert.Contains("\"directory\"", json.StdOut, "json directory");
                MuxAssert.Contains(folder + "/scripts/go.sh", json.StdOut, "json body resolved");
            });

            // --- bundled defaults ---
            AddSync("BundledSeedingFirstRunAndUpgrade", "Bundled folder skills seed on first run with their files, top up on upgrade, and respect edits and deletions", (string dir) =>
            {
                string source = Path.Combine(dir, "bundled");
                Write(Path.Combine(source, "zz-first-bundled"), "SKILL.md", "---\nname: zz-first-bundled\ndescription: First.\n---\n\nBody.\n");
                Write(Path.Combine(source, "zz-first-bundled"), "scripts/tool.py", "print(1)\n");
                Write(Path.Combine(source, "zz-first-bundled"), "assets/logo.bin", "\u0001\u0002binary");
                string skills = Path.Combine(dir, "skills");
                IReadOnlyList<string> first = DefaultSkillLibrary.SeedNewInto(skills, BundledSkillSet.FromDirectory(source));
                MuxAssert.IsTrue(first.Contains("zz-first-bundled"), "seeded on first run");
                MuxAssert.IsTrue(File.Exists(Path.Combine(skills, "zz-first-bundled", "assets", "logo.bin")), "binary asset written");
                if (!OperatingSystem.IsWindows())
                {
                    UnixFileMode mode = File.GetUnixFileMode(Path.Combine(skills, "zz-first-bundled", "scripts", "tool.py"));
                    MuxAssert.IsTrue((mode & UnixFileMode.UserExecute) != 0, "script is executable");
                }

                File.WriteAllText(Path.Combine(skills, "zz-first-bundled", "SKILL.md"), "---\nname: zz-first-bundled\ndescription: Edited.\n---\n\nMine.\n");
                Write(Path.Combine(source, "zz-second-bundled"), "SKILL.md", "---\nname: zz-second-bundled\ndescription: Second.\n---\n\nBody.\n");
                IReadOnlyList<string> upgrade = DefaultSkillLibrary.SeedNewInto(skills, BundledSkillSet.FromDirectory(source));
                MuxAssert.AreEqual("zz-second-bundled", string.Join(",", upgrade), "only the new skill is added");
                MuxAssert.Contains("Edited.", File.ReadAllText(Path.Combine(skills, "zz-first-bundled", "SKILL.md")), "user edit preserved");
                Directory.Delete(Path.Combine(skills, "zz-second-bundled"), true);
                MuxAssert.AreEqual(0, DefaultSkillLibrary.SeedNewInto(skills, BundledSkillSet.FromDirectory(source)).Count, "deleted skill not resurrected");
                MuxAssert.IsFalse(Directory.Exists(Path.Combine(skills, "zz-second-bundled")), "still gone");
                Skill loaded = new SkillLoader(skills).Load(Path.Combine(skills, "zz-first-bundled"));
                MuxAssert.IsTrue(loaded.IsValid, "seeded skill loads");
                string simple = Path.Combine(dir, "simple");
                DefaultSkillLibrary.SeedInto(simple, BundledSkillSet.FromDirectory(source));
                MuxAssert.IsTrue(File.Exists(Path.Combine(simple, "zz-second-bundled", "SKILL.md")), "SeedInto writes bundled skills too");
            });
            AddSync("BundledRules", "Bundled skills need a SKILL.md, may not reuse a default id, and folders without SKILL.md are ignored", (string dir) =>
            {
                MuxAssert.Throws<ArgumentException>(() => new BundledSkill("x", new Dictionary<string, byte[]> { ["readme.md"] = new byte[0] }), "SKILL.md required");
                MuxAssert.Throws<ArgumentException>(() => new BundledSkill(" ", new Dictionary<string, byte[]> { ["SKILL.md"] = new byte[0] }), "id required");
                string source = Path.Combine(dir, "bundled");
                Write(Path.Combine(source, "git-commit"), "SKILL.md", "---\nname: git-commit\ndescription: Clash.\n---\n");
                Write(Path.Combine(source, "no-skill-md"), "notes.txt", "x");
                BundledSkillSet set = BundledSkillSet.FromDirectory(source);
                MuxAssert.AreEqual(1, set.Skills.Count, "folder without SKILL.md ignored");
                MuxAssert.Throws<InvalidOperationException>(() => DefaultSkillLibrary.All(set), "a bundled id that clashes with a default is rejected");
                MuxAssert.AreEqual(0, BundledSkillSet.FromDirectory(Path.Combine(dir, "missing")).Skills.Count, "missing folder is empty");
                MuxAssert.Throws<InvalidOperationException>(() => SkillFileWriter.WriteAll(Path.Combine(dir, "w"), new Dictionary<string, byte[]> { ["../escape.txt"] = new byte[0] }, "x"), "paths cannot escape");
                MuxAssert.IsTrue(SkillFileWriter.ShouldBeExecutable("scripts/a.txt", new byte[0]), "scripts folder");
                MuxAssert.IsTrue(SkillFileWriter.ShouldBeExecutable("tools/x", Encoding.ASCII.GetBytes("#!/bin/sh")), "shebang");
                MuxAssert.IsFalse(SkillFileWriter.ShouldBeExecutable("references/a.md", Encoding.ASCII.GetBytes("# Title")), "a heading is not a shebang");
            });
            AddSync("EmbeddedBundledAndPacksAreClean", "Every embedded bundled and pack skill validates, has no em-dash, and has a unique id", (string dir) =>
            {
                IReadOnlyDictionary<string, string> all = DefaultSkillLibrary.All();
                foreach (BundledSkill skill in BundledSkillSet.Embedded.Skills)
                {
                    MuxAssert.IsTrue(all.ContainsKey(skill.Id), skill.Id + " is part of the default library");
                }

                string skills = Path.Combine(dir, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                foreach (BundledSkill skill in BundledSkillSet.Embedded.Skills)
                {
                    Skill loaded = new SkillLoader(skills).Load(Path.Combine(skills, skill.Id));
                    MuxAssert.IsTrue(loaded.IsValid, "bundled " + skill.Id + " valid: " + string.Join("; ", loaded.Validation.Errors));
                    AssertNoDashes(skill);
                }

                HashSet<string> packIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (SkillPack pack in SkillPackCatalog.Embedded.Packs)
                {
                    MuxAssert.IsTrue(pack.Title.Length > 0, pack.Id + " has a title");
                    string packDir = Path.Combine(dir, "packs", pack.Id);
                    foreach (BundledSkill skill in pack.Skills)
                    {
                        MuxAssert.IsTrue(packIds.Add(skill.Id), "pack skill id " + skill.Id + " is unique across packs");
                        MuxAssert.IsFalse(all.ContainsKey(skill.Id), "pack skill " + skill.Id + " does not reuse a default id");
                        SkillFileWriter.WriteAll(Path.Combine(packDir, skill.Id), skill.Files, skill.Id);
                        Skill loaded = new SkillLoader(packDir).Load(Path.Combine(packDir, skill.Id));
                        MuxAssert.IsTrue(loaded.IsValid, "pack " + pack.Id + "/" + skill.Id + " valid: " + string.Join("; ", loaded.Validation.Errors));
                        AssertNoDashes(skill);
                    }
                }
            });

            // --- packs ---
            AddSync("CatalogReadsMetadata", "The pack catalog reads pack.json, tolerates a missing or malformed one, and sorts skills", (string dir) =>
            {
                string packs = Path.Combine(dir, "packs");
                MakePack(packs, "alpha", "{\"title\":\"Alpha Pack\",\"description\":\"First pack.\",\"category\":\"engineering\",\"source\":\"https://example.com/repo\",\"license\":\"MIT\"}", "zeta-skill", "beta-skill");
                MakePack(packs, "bravo", null, "one-skill");
                MakePack(packs, "charlie", "{not json", "two-skill");
                Write(Path.Combine(packs, "alpha", "not-a-skill"), "notes.txt", "x");
                SkillPackCatalog catalog = SkillPackCatalog.FromDirectory(packs);
                MuxAssert.AreEqual("alpha,bravo,charlie", string.Join(",", System.Linq.Enumerable.Select(catalog.Packs, (SkillPack p) => p.Id)), "packs sorted");
                SkillPack alpha = catalog.Find("ALPHA")!;
                MuxAssert.AreEqual("Alpha Pack", alpha.Title, "title");
                MuxAssert.AreEqual("engineering", alpha.Category, "category");
                MuxAssert.AreEqual("MIT", alpha.License, "license");
                MuxAssert.AreEqual("https://example.com/repo", alpha.Source, "source");
                MuxAssert.AreEqual("beta-skill,zeta-skill", string.Join(",", System.Linq.Enumerable.Select(alpha.Skills, (BundledSkill s) => s.Id)), "skills sorted, non-skill folder ignored");
                MuxAssert.AreEqual("bravo", catalog.Find("bravo")!.Title, "missing pack.json titles by id");
                MuxAssert.AreEqual("charlie", catalog.Find("charlie")!.Title, "malformed pack.json falls back");
                MuxAssert.IsNull(catalog.Find("delta"), "unknown pack");
                MuxAssert.IsNotNull(alpha.Find("BETA-SKILL"), "skill lookup is case-insensitive");
                MuxAssert.AreEqual(0, SkillPackCatalog.FromDirectory(Path.Combine(dir, "nope")).Packs.Count, "missing folder");
            });
            AddSync("InstallAndRemoveLifecycle", "Installing writes files and the manifest; removing takes away only what the pack installed", (string dir) =>
            {
                SkillPackCatalog catalog = DemoCatalog(dir);
                string skills = Path.Combine(dir, "skills");
                SkillPackInstaller installer = new SkillPackInstaller(skills, catalog);
                SkillPackResult installed = installer.Install("demo");
                MuxAssert.AreEqual("demo-one,demo-two", string.Join(",", installed.Installed), "both installed");
                MuxAssert.IsTrue(File.Exists(Path.Combine(skills, "demo-one", "scripts", "run.sh")), "files written");
                MuxAssert.IsTrue(installer.IsInstalled("demo", "demo-one"), "recorded");
                MuxAssert.AreEqual(2, installer.InstalledCount("DEMO"), "count");
                MuxAssert.IsTrue(File.Exists(Path.Combine(skills, SkillPackInstaller.ManifestFileName)), "manifest written");
                MuxAssert.IsTrue(new SkillLoader(skills).Load(Path.Combine(skills, "demo-one")).IsValid, "installed skill loads");

                SkillPackResult again = installer.Install("demo");
                MuxAssert.AreEqual(0, again.Installed.Count, "second install changes nothing");
                MuxAssert.Contains("pass --force", string.Join(";", again.Skipped), "explains force");

                File.WriteAllText(Path.Combine(skills, "demo-one", "SKILL.md"), "---\nname: demo-one\ndescription: My edit.\n---\n");
                SkillPackResult keep = installer.Remove("demo");
                MuxAssert.AreEqual("demo-two", string.Join(",", keep.Removed), "unedited removed");
                MuxAssert.Contains("edited since it was installed", string.Join(";", keep.Skipped), "edited kept");
                MuxAssert.IsTrue(Directory.Exists(Path.Combine(skills, "demo-one")), "edited folder still there");
                SkillPackResult forced = installer.Install("demo", "demo-one", force: true);
                MuxAssert.AreEqual("demo-one", string.Join(",", forced.Installed), "force reinstalls");
                MuxAssert.DoesNotContain("My edit.", File.ReadAllText(Path.Combine(skills, "demo-one", "SKILL.md")), "edit replaced by force");
                File.WriteAllText(Path.Combine(skills, "demo-one", "SKILL.md"), "---\nname: demo-one\ndescription: Edited again.\n---\n");
                SkillPackResult forcedRemove = installer.Remove("demo", "demo-one", force: true);
                MuxAssert.AreEqual("demo-one", string.Join(",", forcedRemove.Removed), "force removes an edited copy");
                MuxAssert.AreEqual(0, installer.InstalledCount("demo"), "nothing left");
            });
            AddSync("InstallNeverTouchesUserSkills", "A user's own skill with a pack skill's name is never replaced or removed, even with force", (string dir) =>
            {
                SkillPackCatalog catalog = DemoCatalog(dir);
                string skills = Path.Combine(dir, "skills");
                Write(Path.Combine(skills, "demo-one"), "SKILL.md", "---\nname: demo-one\ndescription: Mine.\n---\n");
                SkillPackInstaller installer = new SkillPackInstaller(skills, catalog);
                SkillPackResult result = installer.Install("demo", force: true);
                MuxAssert.AreEqual("demo-two", string.Join(",", result.Installed), "only the free name installed");
                MuxAssert.Contains("not installed by a pack", string.Join(";", result.Skipped), "explains");
                MuxAssert.Contains("Mine.", File.ReadAllText(Path.Combine(skills, "demo-one", "SKILL.md")), "user skill intact");
                SkillPackResult removed = installer.Remove("demo", force: true);
                MuxAssert.AreEqual("demo-two", string.Join(",", removed.Removed), "only the pack's skill removed");
                MuxAssert.IsTrue(Directory.Exists(Path.Combine(skills, "demo-one")), "user skill survives removal");
                SkillPackResult notOurs = installer.Remove("demo", "demo-one");
                MuxAssert.Contains("not installed from the demo pack", string.Join(";", notOurs.Skipped), "single remove explains");
            });
            AddSync("InstallErrorsAndEdges", "Unknown packs and skills fail clearly; a vanished folder is forgotten; a corrupt manifest removes nothing", (string dir) =>
            {
                SkillPackCatalog catalog = DemoCatalog(dir);
                string skills = Path.Combine(dir, "skills");
                SkillPackInstaller installer = new SkillPackInstaller(skills, catalog);
                MuxAssert.Throws<KeyNotFoundException>(() => installer.Install("nope"), "unknown pack");
                MuxAssert.Throws<KeyNotFoundException>(() => installer.Install("demo", "nope"), "unknown skill");
                MuxAssert.Throws<KeyNotFoundException>(() => installer.Remove("nope"), "unknown pack to remove");
                MuxAssert.Throws<KeyNotFoundException>(() => installer.Remove("demo", "nope"), "unknown skill to remove");
                MuxAssert.Throws<ArgumentException>(() => new SkillPackInstaller(" ", catalog), "directory required");
                installer.Install("demo", "demo-two");
                Directory.Delete(Path.Combine(skills, "demo-two"), true);
                MuxAssert.IsFalse(installer.IsInstalled("demo", "demo-two"), "a vanished folder is not installed");
                SkillPackResult gone = installer.Remove("demo");
                MuxAssert.Contains("already gone; forgotten", string.Join(";", gone.Skipped), "forgotten");
                installer.Install("demo", "demo-one");
                File.WriteAllText(Path.Combine(skills, SkillPackInstaller.ManifestFileName), "{corrupt");
                MuxAssert.Throws<KeyNotFoundException>(() => new SkillPackInstaller(skills, new SkillPackCatalog(null)).Remove("demo"), "with a corrupt manifest and no catalog entry, the pack is unknown");
                SkillPackResult safe = installer.Remove("demo", "demo-one");
                MuxAssert.AreEqual(0, safe.Removed.Count, "a corrupt manifest removes nothing");
                MuxAssert.IsTrue(Directory.Exists(Path.Combine(skills, "demo-one")), "folder kept");
            });

            // --- importer rules ---
            AddSync("DashRules", "Dashes become colons, commas, or hyphens by context, and none survive", (string dir) =>
            {
                string input = "# Step 1 \u2014 Setup\n- **Speed** \u2014 quick pass\nThis library parses input in several steps \u2014 which is fast \u2014 and reports.\nPages 10\u201320 and 3 \u2014 5.\nNote this \u2014\n\u2014 Ada Lovelace\nself\u2013contained\n```\nx = a \u2014 b\n```\n| Col \u2014 one | b |";
                string output = SkillImportNormalizer.ReplaceDashes(input, prose: true);
                MuxAssert.Contains("# Step 1: Setup", output, "heading colon");
                MuxAssert.Contains("- **Speed**: quick pass", output, "label colon");
                MuxAssert.Contains("several steps, which is fast, and reports.", output, "long clauses take commas");
                MuxAssert.Contains("Pages 10-20 and 3-5.", output, "numeric ranges hyphenated");
                MuxAssert.Contains("Note this:", output, "trailing dash becomes a colon");
                MuxAssert.Contains("\nAda Lovelace\n", output, "leading attribution dash dropped");
                MuxAssert.Contains("self-contained", output, "unspaced en dash");
                MuxAssert.Contains("x = a - b", output, "code gets a hyphen");
                MuxAssert.Contains("| Col: one | b |", output, "table cell");
                MuxAssert.IsFalse(output.Contains('\u2014') || output.Contains('\u2013'), "no dash survives");
                MuxAssert.AreEqual("a-b-c", SkillImportNormalizer.ReplaceDashes("a\u2014b\u2013c", prose: false), "code mode");
                string script = SkillImportNormalizer.NormalizeScript("print('a \u2014 b')", out int changed);
                MuxAssert.AreEqual(1, changed, "script dash counted");
                MuxAssert.AreEqual("print('a - b')", script, "script dash");
            });
            AddSync("PathRules", "Placeholders and the skill's own folders are anchored to ${SKILL_DIR}; other paths are left alone", (string dir) =>
            {
                string input = "Run `python scripts/audit.py src/` then `bash ./scripts/fix.sh`.\nSee references/guide.md and templates/base.md.\nUse ${CLAUDE_SKILL_DIR}/x and {baseDir}/y and $SKILL_ROOT/z and ${CLAUDE_PLUGIN_ROOT}/w.\nKeep https://example.com/scripts/a.py, src/scripts/b.py, ${SKILL_DIR}/scripts/c.py and assets/logo.png.";
                string output = SkillImportNormalizer.NormalizeMarkdown(input, new[] { "scripts", "references" }, out List<string> changes, out _);
                MuxAssert.Contains("`python \"${SKILL_DIR}/scripts/audit.py\" src/`", output, "runner path quoted and anchored");
                MuxAssert.Contains("`bash \"${SKILL_DIR}/scripts/fix.sh\"`", output, "./ prefix handled");
                MuxAssert.Contains("See ${SKILL_DIR}/references/guide.md and templates/base.md.", output, "only existing folders anchored");
                MuxAssert.Contains("Use ${SKILL_DIR}/x and ${SKILL_DIR}/y and ${SKILL_DIR}/z and ${SKILL_DIR}/w.", output, "aliases rewritten");
                MuxAssert.Contains("https://example.com/scripts/a.py, src/scripts/b.py, ${SKILL_DIR}/scripts/c.py and assets/logo.png.", output, "URLs, nested paths, and anchored paths untouched");
                MuxAssert.Contains("folder placeholders rewritten to ${SKILL_DIR} (4)", string.Join(";", changes), "placeholder count");
                MuxAssert.Contains("bundled file paths anchored to ${SKILL_DIR} (3)", string.Join(";", changes), "anchor count");
                string twice = SkillImportNormalizer.NormalizeMarkdown(output, new[] { "scripts", "references" }, out List<string> second, out _);
                MuxAssert.AreEqual(output, twice, "normalizing twice changes nothing");
                MuxAssert.AreEqual(0, second.Count, "no changes the second time");
            });
            AddSync("ClaudeReferenceRules", "CLAUDE.md, ~/.claude, claude -p, and tool names are rewritten; unsupported features are flagged", (string dir) =>
            {
                string input = "Read CLAUDE.md first and `CLAUDE.md` too.\nStore in ~/.claude/notes and run claude -p \"x\".\nUse the Bash tool, the Read tool, WebFetch, TodoWrite, and AskUserQuestion.\nSchedule with CronCreate and edit .claude/settings.json.\n```\ncat CLAUDE.md\n```\nKeep docs/CLAUDE.md as a path.";
                string output = SkillImportNormalizer.NormalizeMarkdown(input, null, out List<string> changes, out List<string> flags);
                MuxAssert.Contains("Read the project instruction file (MUX.md, AGENTS.md, or CLAUDE.md) first and `MUX.md` too.", output, "prose and inline code");
                MuxAssert.Contains("Store in ~/.mux/notes and run mux print \"x\".", output, "home and print");
                MuxAssert.Contains("Use the run_process tool, the read_file tool, web_retrieve, plan_tasks, and ask_user.", output, "tools");
                MuxAssert.Contains("cat CLAUDE.md", output, "fenced code untouched");
                MuxAssert.Contains("docs/CLAUDE.md", output, "paths untouched");
                string flagText = string.Join(";", flags);
                MuxAssert.Contains("line 4: CronCreate has no direct mux equivalent", flagText, "cron flagged with its line");
                MuxAssert.Contains(".claude/settings.json", flagText, "settings flagged");
                MuxAssert.Contains("CLAUDE.md in a code block", flagText, "fenced reference flagged");
                string again = SkillImportNormalizer.NormalizeMarkdown(output, null, out List<string> secondChanges, out _);
                MuxAssert.AreEqual(output, again, "the instruction phrase is stable on a second run");
                MuxAssert.AreEqual(0, secondChanges.Count, "nothing more to change");
            });
            AddSync("FrontmatterRules", "Missing category, source, and license are added once; files without frontmatter get one", (string dir) =>
            {
                string with = "---\nname: x\ndescription: Does things.\nlicense: Apache-2.0\n---\n\nBody.\n";
                string output = SkillImportNormalizer.EnsureFrontmatter(with, "x", "testing", "https://e.com/r@abc#x", "MIT", out List<string> changes);
                MuxAssert.Contains("category: testing\n", output, "category added");
                MuxAssert.Contains("source: \"https://e.com/r@abc#x\"\n", output, "source added and quoted");
                MuxAssert.Contains("license: Apache-2.0\n", output, "existing license kept");
                MuxAssert.DoesNotContain("license: MIT", output, "not duplicated");
                MuxAssert.AreEqual(2, changes.Count, "two additions");
                MuxAssert.AreEqual(output, SkillImportNormalizer.EnsureFrontmatter(output, "x", "testing", "https://e.com/r@abc#x", "MIT", out List<string> none), "idempotent");
                MuxAssert.AreEqual(0, none.Count, "no further changes");
                string bare = SkillImportNormalizer.EnsureFrontmatter("# Title\n\nFirst line here.\n", "bare", "data", null, null, out List<string> created);
                MuxAssert.IsTrue(bare.StartsWith("---\nname: bare\ndescription: First line here.\ncategory: data\n---\n", StringComparison.Ordinal), "frontmatter created: " + bare);
                MuxAssert.Contains("frontmatter created", string.Join(";", created), "reported");
                MuxAssert.AreEqual("testing", SkillImportNormalizer.ReadFrontmatterValue(output, "CATEGORY"), "read value case-insensitively");
                MuxAssert.AreEqual("https://e.com/r@abc#x", SkillImportNormalizer.ReadFrontmatterValue(output, "source"), "quotes removed");
                MuxAssert.IsNull(SkillImportNormalizer.ReadFrontmatterValue("no frontmatter", "name"), "none");
            });
            AddSync("CategoryInference", "Category comes from the pack, then the source path, then the description, then general", (string dir) =>
            {
                MuxAssert.AreEqual("security", SkillImportNormalizer.InferCategory("Security", "marketing-skill/x", "seo"), "pack wins");
                MuxAssert.AreEqual("marketing", SkillImportNormalizer.InferCategory(null, "marketing-skill/seo-audit", null), "path keyword");
                MuxAssert.AreEqual("business", SkillImportNormalizer.InferCategory(null, "c-level-advisor/cfo", null), "c-level");
                MuxAssert.AreEqual("testing", SkillImportNormalizer.InferCategory(null, "misc/x", "Generate Playwright tests."), "description keyword");
                MuxAssert.AreEqual("general", SkillImportNormalizer.InferCategory(null, "misc/x", "Does a thing."), "fallback");
                MuxAssert.AreEqual("my-skill", SkillImporter.ToSkillId(" My_Skill "), "id from folder");
                MuxAssert.AreEqual("a-b", SkillImporter.ToSkillId("a!!b"), "unsafe characters collapse");
                MuxAssert.AreEqual(string.Empty, SkillImporter.ToSkillId("!!!"), "nothing usable");
                MuxAssert.IsTrue(SkillImporter.IsGitUrl("https://github.com/a/b") && SkillImporter.IsGitUrl("git@github.com:a/b.git") && SkillImporter.IsGitUrl("/tmp/repo.git"), "git URLs");
                MuxAssert.IsFalse(SkillImporter.IsGitUrl("/tmp/skills"), "a folder is not a URL");
            });

            // --- importer end to end ---
            AddSync("ImportFolderEndToEnd", "Importing a folder finds every skill, skips mirrors and duplicates, normalizes, validates, and reports", (string dir) =>
            {
                string source = Path.Combine(dir, "repo");
                Write(source, "LICENSE", "MIT License\n\nPermission is hereby granted, free of charge, to any person\n");
                Write(Path.Combine(source, "engineering-team", "code-auditor"), "SKILL.md", "---\nname: code-auditor\ndescription: Audit code \u2014 fast\n---\n\nRun `python scripts/audit.py` and read CLAUDE.md.\n");
                Write(Path.Combine(source, "engineering-team", "code-auditor", "scripts"), "audit.py", "print('done \u2014 ok')\n");
                Write(Path.Combine(source, "marketing-skill", "seo-check"), "SKILL.md", "---\nname: seo-check\ndescription: Check SEO.\n---\n\nBody.\n");
                Write(Path.Combine(source, ".gemini", "skills", "code-auditor"), "SKILL.md", "---\nname: code-auditor\ndescription: mirror\n---\n");
                Write(Path.Combine(source, "other", "code-auditor"), "SKILL.md", "---\nname: code-auditor\ndescription: duplicate\n---\n");
                Write(Path.Combine(source, "broken", "bad-name"), "SKILL.md", "---\nname: Totally Different\ndescription: x\n---\n");
                string skills = Path.Combine(dir, "skills");
                List<SkillImportReport> reports = new SkillImporter().Import(source, skills, null, false, false, false, CancellationToken.None);
                MuxAssert.AreEqual(4, reports.Count, "mirror skipped, four folders found: " + string.Join(",", reports.ConvertAll(r => r.SourcePath)));
                SkillImportReport auditor = reports.Find(r => r.SourcePath == "engineering-team/code-auditor")!;
                MuxAssert.AreEqual("imported", auditor.Status, "imported: " + string.Join(";", auditor.Errors));
                MuxAssert.AreEqual("engineering", auditor.Category, "category from path");
                string md = File.ReadAllText(Path.Combine(skills, "code-auditor", "SKILL.md"));
                MuxAssert.Contains("license: MIT", md, "license detected");
                MuxAssert.Contains("${SKILL_DIR}/scripts/audit.py", md, "path anchored");
                MuxAssert.Contains("the project instruction file", md, "CLAUDE.md rewritten");
                MuxAssert.IsFalse(md.Contains('\u2014'), "no em-dash in SKILL.md");
                MuxAssert.AreEqual("print('done - ok')\n", File.ReadAllText(Path.Combine(skills, "code-auditor", "scripts", "audit.py")), "script dash replaced");
                MuxAssert.AreEqual("skipped", reports.Find(r => r.SourcePath == "other/code-auditor")!.Status, "duplicate id skipped");
                MuxAssert.AreEqual("marketing", reports.Find(r => r.Id == "seo-check")!.Category, "marketing category");
                SkillImportReport bad = reports.Find(r => r.Id == "bad-name")!;
                MuxAssert.AreEqual("invalid", bad.Status, "name mismatch reported invalid");
                MuxAssert.IsTrue(bad.Errors.Count > 0, "with errors");

                List<SkillImportReport> rerun = new SkillImporter().Import(source, skills, null, false, false, false, CancellationToken.None);
                MuxAssert.AreEqual("skipped", rerun.Find(r => r.Id == "code-auditor" && r.SourcePath.StartsWith("engineering", StringComparison.Ordinal))!.Status, "existing skipped without force");
                List<SkillImportReport> forced = new SkillImporter().Import(source, skills, null, false, false, true, CancellationToken.None);
                MuxAssert.AreEqual("imported", forced.Find(r => r.SourcePath == "engineering-team/code-auditor")!.Status, "force replaces");
            });
            AddSync("ImportDryRunPackLayoutAndErrors", "A dry run writes nothing; the pack layout writes pack.json; bad sources fail", (string dir) =>
            {
                string source = Path.Combine(dir, "one");
                Write(source, "SKILL.md", "---\nname: one\ndescription: Single.\n---\n\nBody.\n");
                string target = Path.Combine(dir, "target");
                List<SkillImportReport> dry = new SkillImporter().Import(source, target, "data", false, true, false, CancellationToken.None);
                MuxAssert.AreEqual("would-import", dry[0].Status, "dry run");
                MuxAssert.AreEqual("data", dry[0].Category, "pack sets the category");
                MuxAssert.IsFalse(Directory.Exists(target), "nothing written");
                string packs = Path.Combine(dir, "packs");
                List<SkillImportReport> laid = new SkillImporter().Import(source, packs, "data", true, false, false, CancellationToken.None);
                MuxAssert.AreEqual("imported", laid[0].Status, "imported into the pack");
                MuxAssert.IsTrue(File.Exists(Path.Combine(packs, "data", "one", "SKILL.md")), "pack folder layout");
                MuxAssert.IsTrue(File.Exists(Path.Combine(packs, "data", "pack.json")), "pack.json written");
                MuxAssert.AreEqual(1, SkillPackCatalog.FromDirectory(packs).Find("data")!.Skills.Count, "the catalog reads the imported pack");
                MuxAssert.Throws<ArgumentException>(() => new SkillImporter().Import(Path.Combine(dir, "missing"), target, null, false, false, false, CancellationToken.None), "missing source");
                MuxAssert.Throws<ArgumentException>(() => new SkillImporter().Import(" ", target, null, false, false, false, CancellationToken.None), "blank source");
                MuxAssert.Throws<ArgumentException>(() => new SkillImporter().Import(source, target, null, true, false, false, CancellationToken.None), "pack layout needs a pack");
                Directory.CreateDirectory(Path.Combine(dir, "empty"));
                MuxAssert.AreEqual(0, new SkillImporter().Import(Path.Combine(dir, "empty"), target, null, false, false, false, CancellationToken.None).Count, "no skills found");
            });
            cases.Add(new TestCaseDescriptor(SuiteId, "ImportFromGit", "Importing a git repository clones it, records the commit as the source, and cleans up", (CancellationToken ct) => WithTempAsync((string dir) =>
            {
                string repoDir = Path.Combine(dir, "skills-repo.git");
                GitFixture repo = new GitFixture(repoDir);
                repo.Write("tools/lint-helper/SKILL.md", "---\nname: lint-helper\ndescription: Lint helper.\n---\n\nBody.\n");
                string commit = repo.Commit("add skill").Substring(0, 12);
                string skills = Path.Combine(dir, "skills");
                List<SkillImportReport> reports = new SkillImporter().Import(repoDir, skills, null, false, false, false, ct);
                MuxAssert.AreEqual("imported", reports[0].Status, "imported: " + string.Join(";", reports[0].Errors));
                string md = File.ReadAllText(Path.Combine(skills, "lint-helper", "SKILL.md"));
                MuxAssert.Contains("source: \"" + repoDir, md, "source recorded");
                MuxAssert.Contains("@" + commit, md, "commit recorded");
                MuxAssert.Contains("#tools/lint-helper", md, "path recorded");
                MuxAssert.Throws<InvalidOperationException>(() => new SkillImporter().Import(Path.Combine(dir, "nope.git"), skills, null, false, false, false, ct), "a failed clone is reported");
                return Task.CompletedTask;
            }), skip: !git, skipReason: "git is not on PATH"));

            // --- surfaces ---
            AddSync("CliPackCommands", "mux skill pack list, show, install, and remove work, and bad usage fails", (string dir) =>
            {
                SkillPackCatalog catalog = DemoCatalog(dir);
                string config = PrepareConfig(dir, out string skills);
                CliInvocationResult list = Capture(() => SkillPackCommand.Run(new[] { "pack", "list", "--config-dir", config }, catalog));
                MuxAssert.AreEqual(0, list.ExitCode, "list ok: " + list.StdErr);
                MuxAssert.Contains("demo", list.StdOut, "pack listed");
                MuxAssert.Contains("0/2", list.StdOut, "installed count");
                CliInvocationResult install = Capture(() => SkillPackCommand.Run(new[] { "pack", "install", "demo", "--skill", "demo-two", "--config-dir", config }, catalog));
                MuxAssert.Contains("Installed 1 skill(s) from the demo pack: demo-two", install.StdOut, "install one");
                CliInvocationResult show = Capture(() => SkillPackCommand.Run(new[] { "pack", "show", "demo", "--config-dir", config, "--output-format", "json" }, catalog));
                MuxAssert.Contains("\"installed\": true", show.StdOut.Replace("\"installed\":true", "\"installed\": true"), "json shows installed: " + show.StdOut);
                CliInvocationResult remove = Capture(() => SkillPackCommand.Run(new[] { "pack", "remove", "demo", "--config-dir", config }, catalog));
                MuxAssert.Contains("Removed 1 skill(s) from the demo pack: demo-two", remove.StdOut, "remove");
                MuxAssert.IsFalse(Directory.Exists(Path.Combine(skills, "demo-two")), "folder gone");
                MuxAssert.AreEqual(1, Capture(() => SkillPackCommand.Run(new[] { "pack", "install", "nope", "--config-dir", config }, catalog)).ExitCode, "unknown pack");
                MuxAssert.AreEqual(1, Capture(() => SkillPackCommand.Run(new[] { "pack", "install", "--config-dir", config }, catalog)).ExitCode, "missing pack");
                MuxAssert.AreEqual(1, Capture(() => SkillPackCommand.Run(new[] { "pack", "frob", "--config-dir", config }, catalog)).ExitCode, "unknown action");
                CliInvocationResult badOption = Capture(() => SkillPackCommand.Run(new[] { "pack", "list", "--bogus" }, catalog));
                MuxAssert.Contains("Unknown option '--bogus'", badOption.StdErr, "unknown option");
                MuxAssert.IsTrue(SkillPackCommand.Handles(new[] { "pack" }) && SkillPackCommand.Handles(new[] { "import", "x" }), "routing");
                MuxAssert.IsFalse(SkillPackCommand.Handles(new[] { "list" }), "other skill verbs are not routed here");
            });
            AddSync("CliImportCommand", "mux skill import imports into the skills directory, supports dry runs, and fails without skills", (string dir) =>
            {
                string config = PrepareConfig(dir, out string skills);
                string source = Path.Combine(dir, "src", "cli-skill");
                Write(source, "SKILL.md", "---\nname: cli-skill\ndescription: From the CLI \u2014 quickly.\n---\n\nBody.\n");
                CliInvocationResult dry = InvokeCli(new[] { "skill", "import", source, "--dry-run", "--config-dir", config });
                MuxAssert.AreEqual(0, dry.ExitCode, "dry run ok: " + dry.StdErr);
                MuxAssert.Contains("would-import", dry.StdOut, "dry run status");
                MuxAssert.IsFalse(Directory.Exists(Path.Combine(skills, "cli-skill")), "dry run wrote nothing");
                CliInvocationResult real = InvokeCli(new[] { "skill", "import", source, "--pack", "research", "--config-dir", config, "--output-format", "json" });
                MuxAssert.AreEqual(0, real.ExitCode, "import ok: " + real.StdOut + real.StdErr);
                MuxAssert.Contains("category: research", File.ReadAllText(Path.Combine(skills, "cli-skill", "SKILL.md")), "category from --pack");
                Directory.CreateDirectory(Path.Combine(dir, "nothing"));
                MuxAssert.AreEqual(1, InvokeCli(new[] { "skill", "import", Path.Combine(dir, "nothing"), "--config-dir", config }).ExitCode, "no skills fails");
                MuxAssert.AreEqual(1, InvokeCli(new[] { "skill", "import", "--config-dir", config }).ExitCode, "missing source fails");
            });
            Add("RestPackRoutes", "The pack REST routes list, show, install, and remove with auth, 400, and 404", async (string dir, CancellationToken ct) =>
            {
                SkillPackCatalog catalog = DemoCatalog(dir);
                string skills = Path.Combine(dir, "skills");
                SkillPackRoutes.CatalogOverride = catalog;
                SkillPackRoutes.SkillsDirectoryOverride = skills;
                MuxServer? server = null;
                int port = 0;
                try
                {
                    RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "packkey" };
                    for (int attempt = 0; attempt < 10 && server == null; attempt++)
                    {
                        port = StubHttpServer.FreeLoopbackPort();
                        rest.Port = port;
                        MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(Path.Combine(dir, ".sessions")), () => new List<EndpointConfig>(), null);
                        try { candidate.Start(); server = candidate; } catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                    }

                    MuxAssert.IsNotNull(server, "server bound");
                    string baseUrl = "http://127.0.0.1:" + port + "/v1.0/api/skills/packs";
                    using (HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
                    {
                        for (int attempt = 0; attempt < 20; attempt++)
                        {
                            try { await http.GetAsync("http://127.0.0.1:" + port + "/v1.0/api/health", ct).ConfigureAwait(false); break; }
                            catch (Exception) { await Task.Delay(100, ct).ConfigureAwait(false); }
                        }

                        using (HttpResponseMessage noKey = await http.GetAsync(baseUrl, ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(401, (int)noKey.StatusCode, "key required");
                        }

                        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "packkey");
                        string list = await http.GetStringAsync(baseUrl, ct).ConfigureAwait(false);
                        MuxAssert.Contains("\"Id\":\"demo\"", list, "listed: " + list);
                        MuxAssert.Contains("\"Count\":1", list, "envelope");
                        using (HttpResponseMessage missing = await http.GetAsync(baseUrl + "/nope", ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(404, (int)missing.StatusCode, "unknown pack 404");
                        }

                        string one = await http.GetStringAsync(baseUrl + "/demo", ct).ConfigureAwait(false);
                        MuxAssert.Contains("\"Skills\":[", one, "skills included");
                        MuxAssert.Contains("\"Installed\":false", one, "not installed yet");
                        using (HttpResponseMessage install = await http.PostAsync(baseUrl + "/install", new StringContent("{\"Pack\":\"demo\",\"Skill\":\"demo-one\"}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            string body = await install.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(200, (int)install.StatusCode, "installed: " + body);
                            MuxAssert.Contains("\"Installed\":[\"demo-one\"]", body, "result");
                        }

                        MuxAssert.IsTrue(Directory.Exists(Path.Combine(skills, "demo-one")), "folder written");
                        using (HttpResponseMessage noPack = await http.PostAsync(baseUrl + "/install", new StringContent("{}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(400, (int)noPack.StatusCode, "pack required");
                        }

                        using (HttpResponseMessage badJson = await http.PostAsync(baseUrl + "/install", new StringContent("{not json", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(400, (int)badJson.StatusCode, "bad JSON");
                        }

                        using (HttpResponseMessage unknownSkill = await http.PostAsync(baseUrl + "/install", new StringContent("{\"Pack\":\"demo\",\"Skill\":\"nope\"}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(404, (int)unknownSkill.StatusCode, "unknown skill 404");
                        }

                        using (HttpResponseMessage remove = await http.PostAsync(baseUrl + "/remove", new StringContent("{\"Pack\":\"demo\"}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            string body = await remove.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                            MuxAssert.AreEqual(200, (int)remove.StatusCode, "removed: " + body);
                            MuxAssert.Contains("\"Removed\":[\"demo-one\"]", body, "remove result");
                        }

                        using (HttpResponseMessage unknownRemove = await http.PostAsync(baseUrl + "/remove", new StringContent("{\"Pack\":\"zzz\"}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                        {
                            MuxAssert.AreEqual(404, (int)unknownRemove.StatusCode, "unknown pack remove 404");
                        }
                    }
                }
                finally
                {
                    SkillPackRoutes.CatalogOverride = null;
                    SkillPackRoutes.SkillsDirectoryOverride = null;
                    server?.Dispose();
                }
            });
            Add("TerminalPacksCommand", "/packs and /skills pack list, show, install, and remove in the terminal", async (string dir, CancellationToken ct) =>
            {
                SkillPackCatalog catalog = DemoCatalog(dir);
                string skills = Path.Combine(dir, "skills");
                Directory.CreateDirectory(skills);
                using (SkillRuntime runtime = new SkillRuntime(skills, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromMinutes(5)))
                {
                    HeadlessBackend backend = new HeadlessBackend(160, 40);
                    await using (JobManager manager = new JobManager(EchoRunner, maxConcurrency: 1))
                    using (MuxTuiApp app = new MuxTuiApp(backend, manager, "demo", ApprovalPolicyEnum.AutoApprove, skillRuntime: runtime))
                    {
                        app.PackCatalog = catalog;
                        Submit(backend, app, "/packs");
                        Submit(backend, app, "/packs show demo");
                        Submit(backend, app, "/packs install demo demo-one");
                        Submit(backend, app, "/skills pack show demo");
                        Submit(backend, app, "/packs install nope");
                        Submit(backend, app, "/packs remove demo");
                        Submit(backend, app, "/packs frob");
                        string transcript = string.Join("\n", app.TranscriptSnapshot());
                        MuxAssert.Contains("Skill packs (installed/total):", transcript, "list");
                        MuxAssert.Contains("demo  0/2  Demo", transcript, "pack line");
                        MuxAssert.Contains("[ ] demo-one: First demo skill.", transcript, "show");
                        MuxAssert.Contains("Installed 1 skill(s) from the demo pack: demo-one", transcript, "install");
                        MuxAssert.Contains("[x] demo-one", transcript, "/skills pack delegates");
                        MuxAssert.Contains("No skill pack named 'nope'", transcript, "unknown pack");
                        MuxAssert.Contains("Removed 1 skill(s) from the demo pack: demo-one", transcript, "remove");
                        MuxAssert.Contains("Usage: /packs", transcript, "usage");
                        MuxAssert.IsFalse(Directory.Exists(Path.Combine(skills, "demo-one")), "removed from disk");
                    }
                }
            });
            AddSync("DashboardPacksPanel", "The dashboard has a Packs button and panel, translated in every language, and its JavaScript parses", (string dir) =>
            {
                string html = DashboardPage.Render(null, "9.9.9-test");
                MuxAssert.Contains("id=\"skills_packs\"", html, "button");
                MuxAssert.Contains("on(\"skills_packs\",openPacks);", html, "wired");
                MuxAssert.Contains("function openPack(id)", html, "pack view");
                MuxAssert.Contains("api(\"/v1.0/api/skills/packs/\"+verb,\"POST\",{Pack:id,Skill:skill})", html, "install and remove call the API");
                int languages = Regex.Matches(html, "\"add\\.skill\":").Count;
                foreach (string key in new[] { "act.packs", "pack.none", "pack.install", "pack.remove", "pack.installAll", "pack.removeAll", "pack.installed", "pack.removed", "pack.skipped" })
                {
                    MuxAssert.AreEqual(languages, Regex.Matches(html, "\"" + Regex.Escape(key) + "\":").Count, key + " in every language");
                }

                string? node = FindOnPath("node");
                if (node == null)
                {
                    return;
                }

                MatchCollection scripts = Regex.Matches(html, "<script(?![^>]*\\bsrc=)[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                string file = Path.Combine(dir, "dashboard.js");
                StringBuilder all = new StringBuilder();
                foreach (Match script in scripts) all.Append(script.Groups[1].Value).Append(";\n");
                File.WriteAllText(file, all.ToString());
                ProcessStartInfo info = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
                info.ArgumentList.Add("--check");
                info.ArgumentList.Add(file);
                using (Process process = Process.Start(info)!)
                {
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit(30000);
                    MuxAssert.AreEqual(0, process.ExitCode, "dashboard JavaScript parses: " + error);
                }
            });

            AddSync("FrontmatterValuesReadBlockScalars", "Frontmatter values read folded (>) and literal (|) YAML blocks, so no pack skill shows '>-' as its description", (string dir) =>
            {
                string folded = "---\nname: x\ndescription: >-\n  First line of the\n  description.\n\n  Second paragraph.\ncategory: data\n---\nBody\n";
                MuxAssert.AreEqual("First line of the description. Second paragraph.", SkillImportNormalizer.ReadFrontmatterValue(folded, "description"), "folded block joined");
                MuxAssert.AreEqual("data", SkillImportNormalizer.ReadFrontmatterValue(folded, "category"), "the next key still reads");
                string literal = "---\nname: x\nnotes: |\n  one\n  two\n---\n";
                MuxAssert.AreEqual("one\ntwo", SkillImportNormalizer.ReadFrontmatterValue(literal, "notes"), "literal block keeps line breaks");
                MuxAssert.AreEqual("plain", SkillImportNormalizer.ReadFrontmatterValue("---\nname: x\ndescription: plain\n---\n", "description"), "plain value");
                MuxAssert.AreEqual("quoted: yes", SkillImportNormalizer.ReadFrontmatterValue("---\nname: x\ndescription: \"quoted: yes\"\n---\n", "description"), "quoted value");
                MuxAssert.AreEqual(string.Empty, SkillImportNormalizer.ReadFrontmatterValue("---\nname: x\ndescription: >\n---\n", "description"), "empty folded block");
                MuxAssert.IsNull(SkillImportNormalizer.ReadFrontmatterValue("no frontmatter", "description"), "no frontmatter");
                MuxAssert.IsNull(SkillImportNormalizer.ReadFrontmatterValue(folded, "missing"), "missing key");
                foreach (SkillPack pack in SkillPackCatalog.Embedded.Packs)
                {
                    foreach (BundledSkill skill in pack.Skills)
                    {
                        string? description = SkillImportNormalizer.ReadFrontmatterValue(skill.SkillMarkdown, "description");
                        MuxAssert.IsTrue(!string.IsNullOrWhiteSpace(description) && description != ">-" && description != ">" && description != "|", pack.Id + "/" + skill.Id + " has a real description: " + description);
                    }
                }
            });

            return new TestSuiteDescriptor(SuiteId, "Skill infrastructure: placeholders, bundled skills, packs, importer, surfaces", cases);
        }

        #endregion

        #region Private-Methods

        private static void AssertNoDashes(BundledSkill skill)
        {
            foreach (KeyValuePair<string, byte[]> file in skill.Files)
            {
                string text = Encoding.UTF8.GetString(file.Value);
                MuxAssert.IsFalse(text.Contains('\u2014'), skill.Id + "/" + file.Key + " has no em-dash");
            }
        }

        private static SkillPackCatalog DemoCatalog(string dir)
        {
            string packs = Path.Combine(dir, "demo-packs");
            MakePack(packs, "demo", "{\"title\":\"Demo\",\"description\":\"Two demo skills.\",\"category\":\"engineering\",\"license\":\"MIT\"}", "demo-one", "demo-two");
            Write(Path.Combine(packs, "demo", "demo-one"), "scripts/run.sh", "#!/bin/sh\necho hi\n");
            return SkillPackCatalog.FromDirectory(packs);
        }

        private static void MakePack(string packs, string id, string? packJson, params string[] skills)
        {
            Directory.CreateDirectory(Path.Combine(packs, id));
            if (packJson != null)
            {
                File.WriteAllText(Path.Combine(packs, id, "pack.json"), packJson);
            }

            int n = 0;
            foreach (string skill in skills)
            {
                n++;
                string description = n == 1 ? "First demo skill." : "Another demo skill.";
                Write(Path.Combine(packs, id, skill), "SKILL.md", "---\nname: " + skill + "\ndescription: " + description + "\n---\n\nBody of " + skill + ".\n");
            }
        }

        private static string PrepareConfig(string dir, out string skills)
        {
            string config = Path.Combine(dir, "cfg");
            skills = Path.Combine(config, "skills");
            Directory.CreateDirectory(config);
            File.WriteAllText(Path.Combine(config, "settings.json"), "{\"skillsDirectory\":" + JsonSerializer.Serialize(skills) + "}");
            return config;
        }

        private static void Write(string root, string relative, string content)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static void Submit(HeadlessBackend backend, MuxTuiApp app, string prompt)
        {
            backend.FeedInput(prompt + "\r");
            app.PumpInputOnce();
        }

        private static async IAsyncEnumerable<AgentEvent> EchoRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield return new AssistantTextEvent { Text = "Echo: " + prompt };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static CliInvocationResult InvokeCli(string[] args)
        {
            return Capture(() => Mux.Cli.Program.Main(args));
        }

        private static CliInvocationResult Capture(Func<int> run)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalErr = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exitCode = run();
                return new CliInvocationResult(exitCode, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-imported-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(Path.GetFullPath(root)).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(root, true);
                }
                catch (Exception)
                {
                }
            }
        }

        private static string? FindOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    string candidate = Path.Combine(directory, name);
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        private static bool IsOnPath(string executable)
        {
            return FindOnPath(executable) != null;
        }

        #endregion
    }
}
