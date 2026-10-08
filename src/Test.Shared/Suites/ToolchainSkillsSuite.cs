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
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Phase 2 toolchain skills. Each case builds a small fixture project (manifests and
    /// lockfiles only), dry-runs one skill command in it with <c>MUX_SKILL_DRY_RUN=1</c>, and asserts the exact
    /// command the skill would run and its exit code. No toolchain other than PowerShell is needed; when pwsh is not
    /// on PATH the cases are skipped.
    /// </summary>
    public static class ToolchainSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "ToolchainSkills";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the toolchain-skills suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the toolchain cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool pwsh = IsOnPath("pwsh");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(SuiteId, "SeedsHelperResource", "Toolchain skills seed resources/mux-skill.ps1 and dot-source it", (CancellationToken ct) => WithTempAsync((string root) =>
                {
                    DefaultSkillLibrary.SeedInto(root);
                    string helper = Path.Combine(root, "js-test", "resources", "mux-skill.ps1");
                    MuxAssert.IsTrue(File.Exists(helper), "helper seeded");
                    MuxAssert.AreEqual(DefaultSkillHelpers.Script, File.ReadAllText(helper), "helper content");
                    Skill skill = new SkillLoader(root).Load(Path.Combine(root, "js-test"));
                    foreach (SkillCommand command in skill.Manifest.Commands)
                    {
                        MuxAssert.IsTrue(skill.CodeBlocks[command.BlockId!].StartsWith(DefaultSkillHelpers.Prelude.TrimEnd('\n'), StringComparison.Ordinal), command.Name + " starts with the prelude");
                    }

                    MuxAssert.AreEqual(1, skill.Manifest.AppliesTo.Count, "js skills gated on package.json");
                    return Task.CompletedTask;
                })),

                new TestCaseDescriptor(SuiteId, "SeedNewIntoWritesResources", "SeedNewInto writes resources for newly shipped skills", (CancellationToken ct) => WithTempAsync((string root) =>
                {
                    DefaultSkillLibrary.SeedNewInto(root);
                    MuxAssert.IsTrue(File.Exists(Path.Combine(root, "py-test", "resources", "mux-skill.ps1")), "py-test helper seeded on upgrade path");
                    MuxAssert.IsTrue(DefaultSkillLibrary.AllResources().ContainsKey("project-detect"), "project-detect ships the helper");
                    return Task.CompletedTask;
                })),

                new TestCaseDescriptor(SuiteId, "ProjectDetectReportsMixedRepo", "project-detect reports ecosystems, CI, deploy files, and skills", async (CancellationToken ct) =>
                {
                    await WithTempAsync(async (string root) =>
                    {
                        string skills = Path.Combine(root, "skills");
                        DefaultSkillLibrary.SeedInto(skills);
                        string project = Path.Combine(root, "project");
                        Directory.CreateDirectory(Path.Combine(project, ".git"));
                        Directory.CreateDirectory(Path.Combine(project, ".github", "workflows"));
                        File.WriteAllText(Path.Combine(project, ".github", "workflows", "ci.yml"), "on: push");
                        File.WriteAllText(Path.Combine(project, "package.json"), "{\"devDependencies\":{\"vitest\":\"1\",\"react\":\"18\"}}");
                        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), string.Empty);
                        File.WriteAllText(Path.Combine(project, "pyproject.toml"), "[project]\nname='x'\n");
                        File.WriteAllText(Path.Combine(project, "Dockerfile"), "FROM scratch");
                        File.WriteAllText(Path.Combine(project, "app.ts"), "export {}");

                        ToolResult result = await RunAsync(skills, "project-detect", "json", new List<string>(), project, false, ct).ConfigureAwait(false);
                        string stdout = ReadField(result, "stdout");
                        MuxAssert.AreEqual(0, ReadExit(result), "exit 0: " + result.Content);
                        using (JsonDocument report = JsonDocument.Parse(stdout))
                        {
                            string text = report.RootElement.GetRawText();
                            MuxAssert.Contains("JavaScript/TypeScript", text, "js ecosystem");
                            MuxAssert.Contains("\"pnpm\"", text, "pnpm manager");
                            MuxAssert.Contains("React", text, "react detected");
                            MuxAssert.Contains("Python", text, "python ecosystem");
                            MuxAssert.Contains("GitHub Actions", text, "ci detected");
                            MuxAssert.Contains("Docker", text, "docker detected");
                            MuxAssert.Contains("py-test", text, "python skills suggested");
                            MuxAssert.Contains("vitest", text, "vitest detected");
                        }
                    }).ConfigureAwait(false);
                }, skip: !pwsh, skipReason: "pwsh is not on PATH")
            };

            foreach (ToolchainCase toolchainCase in Cases())
            {
                ToolchainCase captured = toolchainCase;
                cases.Add(new TestCaseDescriptor(SuiteId, captured.Id, captured.Name, (CancellationToken ct) => RunCaseAsync(captured, ct), skip: !pwsh, skipReason: "pwsh is not on PATH"));
            }

            return new TestSuiteDescriptor(SuiteId, "Toolchain skills (JavaScript, Python, project detection) in dry-run mode", cases);
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<ToolchainCase> Cases()
        {
            // --- JavaScript and TypeScript ---
            yield return Js("NpmRunsTestScript", "npm projects run the test script", "{\"scripts\":{\"test\":\"jest\"},\"devDependencies\":{\"jest\":\"29\"}}", "package-lock.json",
                "js-test", "all", null, "DRYRUN: npm run test");
            yield return Js("PnpmFrozenInstall", "pnpm ci installs with a frozen lockfile", "{}", "pnpm-lock.yaml", "js-install", "ci", null, "DRYRUN: pnpm install --frozen-lockfile");
            yield return new ToolchainCase
            {
                Id = "YarnBerryImmutable",
                Name = "Yarn Berry ci uses --immutable",
                Fixture = (string dir) => { Write(dir, "package.json", "{}"); Write(dir, "yarn.lock", string.Empty); Write(dir, ".yarnrc.yml", "nodeLinker: node-modules"); },
                Skill = "js-install",
                Command = "ci",
                Expected = new List<string> { "DRYRUN: yarn install --immutable" }
            };
            yield return Js("BunRunsBuild", "bun projects run the build script with bun", "{\"scripts\":{\"build\":\"tsc\"}}", "bun.lock", "js-build", "build", null, "DRYRUN: bun run build");
            yield return Js("PackageManagerFieldWins", "The packageManager field beats the lockfile", "{\"packageManager\":\"pnpm@9.1.0\"}", "package-lock.json", "js-install", "install", null, "DRYRUN: pnpm install");
            yield return Js("VitestFilter", "Vitest filters run vitest run <pattern>", "{\"devDependencies\":{\"vitest\":\"2\"}}", "pnpm-lock.yaml", "js-test", "filter", "auth", "DRYRUN: pnpm exec vitest run auth");
            yield return Js("JestCoverage", "Jest coverage runs through npx", "{\"devDependencies\":{\"jest\":\"29\"}}", "package-lock.json", "js-test", "coverage", null, "DRYRUN: npx --no-install jest --ci --coverage");
            yield return Js("NodeTestFallback", "Without a runner or test script, node --test is used", "{\"name\":\"plain\"}", "package-lock.json", "js-test", "all", null, "DRYRUN: node --test");
            yield return Js("BiomeLint", "Biome projects lint with biome", "{\"devDependencies\":{\"@biomejs/biome\":\"1\"}}", "package-lock.json", "js-lint", "check", null, "DRYRUN: npx --no-install biome lint .");
            yield return new ToolchainCase
            {
                Id = "NoLinterExits2",
                Name = "A project without a linter exits 2",
                Fixture = (string dir) => Write(dir, "package.json", "{}"),
                Skill = "js-lint",
                Command = "check",
                Expected = new List<string> { "no linter is configured" },
                ExpectedExit = 2
            };
            yield return new ToolchainCase
            {
                Id = "TypecheckWithoutTsconfig",
                Name = "Type-checking without tsconfig.json is a no-op",
                Fixture = (string dir) => Write(dir, "package.json", "{}"),
                Skill = "js-typecheck",
                Command = "check",
                Expected = new List<string> { "nothing to type-check" }
            };
            yield return Js("PrettierVerify", "Prettier verify runs prettier --check", "{\"devDependencies\":{\"prettier\":\"3\"}}", "package-lock.json", "js-format", "verify", null, "DRYRUN: npx --no-install prettier --check .");
            yield return Js("NpmExplain", "js-deps why uses npm explain", "{}", "package-lock.json", "js-deps", "why", "react", "DRYRUN: npm explain react");
            yield return new ToolchainCase
            {
                Id = "NpmScriptExtraArgs",
                Name = "npm passes extra script arguments after --",
                Fixture = (string dir) => { Write(dir, "package.json", "{\"scripts\":{\"gen\":\"node gen.js\"}}"); Write(dir, "package-lock.json", "{}"); },
                Skill = "js-scripts",
                Command = "run",
                Arguments = new List<string> { "gen", "--force" },
                Expected = new List<string> { "DRYRUN: npm run gen -- --force" }
            };
            yield return new ToolchainCase
            {
                Id = "NotAJsProject",
                Name = "A directory without package.json exits 2",
                Fixture = (string dir) => Write(dir, "README.md", "hi"),
                Skill = "js-test",
                Command = "all",
                Expected = new List<string> { "no package.json" },
                ExpectedExit = 2
            };
            yield return new ToolchainCase
            {
                Id = "MonorepoUsesRootLockfile",
                Name = "A package in a pnpm workspace uses the root lockfile",
                Fixture = (string dir) => { Write(dir, "pnpm-lock.yaml", string.Empty); Write(dir, "package.json", "{}"); Write(Path.Combine(dir, "packages", "app"), "package.json", "{\"scripts\":{\"build\":\"vite build\"}}"); },
                RunIn = Path.Combine("packages", "app"),
                Skill = "js-build",
                Command = "build",
                Expected = new List<string> { "DRYRUN: pnpm run build" }
            };

            // --- Python ---
            yield return Py("UvPytest", "uv projects run pytest through uv", (string dir) => { Write(dir, "pyproject.toml", "[project]\nname='x'\n"); Write(dir, "uv.lock", string.Empty); },
                "py-test", "all", null, "DRYRUN: uv run python -m pytest");
            yield return Py("PoetryInstall", "Poetry projects install with poetry", (string dir) => { Write(dir, "pyproject.toml", "[tool.poetry]\nname='x'\n"); Write(dir, "poetry.lock", string.Empty); },
                "py-install", "install", null, "DRYRUN: poetry install");
            yield return Py("PoetryWithoutLock", "[tool.poetry] alone selects poetry", (string dir) => Write(dir, "pyproject.toml", "[tool.poetry]\nname='x'\n"),
                "py-test", "last-failed", null, "DRYRUN: poetry run python -m pytest --lf");
            yield return Py("PipenvAdd", "Pipenv projects add packages with pipenv install", (string dir) => Write(dir, "Pipfile", "[packages]\n"),
                "py-install", "add", "requests", "DRYRUN: pipenv install requests");
            yield return new ToolchainCase
            {
                Id = "PipUsesProjectVenv",
                Name = "Plain pip projects run inside .venv",
                Fixture = (string dir) =>
                {
                    Write(dir, "requirements.txt", "requests\n");
                    string venvPython = OperatingSystem.IsWindows() ? Path.Combine(".venv", "Scripts", "python.exe") : Path.Combine(".venv", "bin", "python");
                    Write(dir, venvPython, string.Empty);
                },
                Skill = "py-test",
                Command = "filter",
                Arguments = new List<string> { "slow" },
                Expected = new List<string> { ".venv", "-m pytest -k slow" }
            };
            yield return Py("PipCreatesVenv", "Without a manager, py-env create makes .venv", (string dir) => Write(dir, "requirements.txt", "requests\n"),
                "py-env", "create", null, "-m venv .venv");
            yield return Py("PyrightWhenConfigured", "pyright is used when configured", (string dir) => { Write(dir, "pyproject.toml", "[project]\nname='x'\n[tool.pyright]\n"); Write(dir, "uv.lock", string.Empty); },
                "py-typecheck", "check", null, "DRYRUN: uv run python -m pyright");
            yield return Py("RuffFormatVerify", "Format verify runs ruff format --check", (string dir) => { Write(dir, "pyproject.toml", "[project]\nname='x'\n"); Write(dir, "uv.lock", string.Empty); },
                "py-format", "verify", null, "DRYRUN: uv run python -m ruff format --check .");
            yield return new ToolchainCase
            {
                Id = "NotAPythonProject",
                Name = "A directory without Python manifests exits 2",
                Fixture = (string dir) => Write(dir, "README.md", "hi"),
                Skill = "py-test",
                Command = "all",
                Expected = new List<string> { "not a Python project" },
                ExpectedExit = 2
            };
        }

        private static ToolchainCase Js(string id, string name, string packageJson, string lockfile, string skill, string command, string? argument, string expected)
        {
            return new ToolchainCase
            {
                Id = id,
                Name = name,
                Fixture = (string dir) => { Write(dir, "package.json", packageJson); Write(dir, lockfile, string.Empty); },
                Skill = skill,
                Command = command,
                Arguments = argument == null ? new List<string>() : new List<string> { argument },
                Expected = new List<string> { expected }
            };
        }

        private static ToolchainCase Py(string id, string name, Action<string> fixture, string skill, string command, string? argument, string expected)
        {
            return new ToolchainCase
            {
                Id = id,
                Name = name,
                Fixture = fixture,
                Skill = skill,
                Command = command,
                Arguments = argument == null ? new List<string>() : new List<string> { argument },
                Expected = new List<string> { expected }
            };
        }

        private static Task RunCaseAsync(ToolchainCase toolchainCase, CancellationToken ct)
        {
            return WithTempAsync(async (string root) =>
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                string project = Path.Combine(root, "project");
                Directory.CreateDirectory(Path.Combine(project, ".git"));
                toolchainCase.Fixture(project);
                string runIn = string.IsNullOrEmpty(toolchainCase.RunIn) ? project : Path.Combine(project, toolchainCase.RunIn);

                ToolResult result = await RunAsync(skills, toolchainCase.Skill, toolchainCase.Command, toolchainCase.Arguments, runIn, true, ct).ConfigureAwait(false);
                string stdout = ReadField(result, "stdout");
                MuxAssert.AreEqual(toolchainCase.ExpectedExit, ReadExit(result), "exit code: " + result.Content);
                foreach (string expected in toolchainCase.Expected)
                {
                    MuxAssert.Contains(expected, stdout, "output of " + toolchainCase.Skill + " " + toolchainCase.Command);
                }
            });
        }

        private static async Task<ToolResult> RunAsync(string skillsDirectory, string skillId, string command, List<string> arguments, string workingDirectory, bool dryRun, CancellationToken ct)
        {
            Skill skill = new SkillLoader(skillsDirectory).Load(Path.Combine(skillsDirectory, skillId));
            MuxAssert.IsTrue(skill.IsValid, skillId + " valid: " + string.Join("; ", skill.Validation.Errors));
            SkillCommand? found = null;
            foreach (SkillCommand candidate in skill.Manifest.Commands)
            {
                if (candidate.Name == command)
                {
                    found = candidate;
                }
            }

            MuxAssert.IsNotNull(found, skillId + " has command " + command);
            Dictionary<string, string> environment = new Dictionary<string, string>();
            if (dryRun)
            {
                environment[DefaultSkillHelpers.DryRunVariable] = "1";
            }

            return await new SkillExecutor().ExecuteAsync("t", skill, found!, arguments, workingDirectory, ct, environment).ConfigureAwait(false);
        }

        private static string ReadField(ToolResult result, string field)
        {
            using (JsonDocument document = JsonDocument.Parse(result.Content))
            {
                return document.RootElement.TryGetProperty(field, out JsonElement value) ? value.GetString() ?? string.Empty : string.Empty;
            }
        }

        private static int ReadExit(ToolResult result)
        {
            using (JsonDocument document = JsonDocument.Parse(result.Content))
            {
                return document.RootElement.TryGetProperty("exit_code", out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : -1;
            }
        }

        private static void Write(string dir, string relative, string content)
        {
            string path = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        private static bool IsOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(Path.Combine(directory, name)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-toolchain-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
