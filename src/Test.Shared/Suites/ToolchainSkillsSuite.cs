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

                new TestCaseDescriptor(SuiteId, "InfrastructureSkillsNeverDestroy", "Container, Kubernetes, cloud, and IaC commands never delete, destroy, prune, or read secret values", (CancellationToken ct) =>
                {
                    string[] forbidden = { "delete", "destroy", "terminate", "prune", "get-secret-value", "with-decryption", "secrets reveal", "--volumes" };
                    HashSet<string> families = new HashSet<string> { "docker", "kubernetes", "openstack", "aws", "azure", "gcp", "digitalocean", "vercel", "netlify", "cloudflare", "fly", "alibaba", "huawei", "ibm-cloud", "linode", "terraform", "pulumi", "rackspace" };
                    int checkedCommands = 0;
                    foreach (DefaultSkillDef definition in DefaultSkillLibrary.Definitions())
                    {
                        bool infrastructure = false;
                        foreach (string tag in definition.Tags)
                        {
                            if (families.Contains(tag)) infrastructure = true;
                        }

                        if (!infrastructure) continue;
                        foreach (DefaultSkillCommandDef command in definition.Commands)
                        {
                            string code = command.Code.Substring(DefaultSkillHelpers.Prelude.Length).ToLowerInvariant();
                            foreach (string word in forbidden)
                            {
                                MuxAssert.IsFalse(code.Contains(word), definition.Id + " " + command.Name + " contains '" + word + "'");
                            }

                            checkedCommands++;
                        }
                    }

                    MuxAssert.IsTrue(checkedCommands > 100, "scanned the infrastructure commands (" + checkedCommands + ")");
                    return Task.CompletedTask;
                }),

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
,
                new TestCaseDescriptor(SuiteId, "ScaffoldsWriteFilesAndRefuseOverwrite", "react-new-component and java-new-class write files and refuse to overwrite", async (CancellationToken ct) =>
                {
                    await WithTempAsync(async (string root) =>
                    {
                        string skills = Path.Combine(root, "skills");
                        DefaultSkillLibrary.SeedInto(skills);

                        string web = Path.Combine(root, "web");
                        Directory.CreateDirectory(Path.Combine(web, ".git"));
                        Write(web, "package.json", "{\"dependencies\":{\"react\":\"18\"}}");
                        Write(Path.Combine(web, "src"), "index.jsx", string.Empty);
                        ToolResult created = await RunAsync(skills, "react-new-component", "create", new List<string> { "Card" }, web, false, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, ReadExit(created), "component created: " + created.Content);
                        string card = Path.Combine(web, "src", "components", "Card.jsx");
                        MuxAssert.IsTrue(File.Exists(card), "Card.jsx written");
                        MuxAssert.Contains("export function Card", File.ReadAllText(card), "component body");
                        ToolResult again = await RunAsync(skills, "react-new-component", "create", new List<string> { "Card" }, web, false, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(1, ReadExit(again), "second create refuses to overwrite");

                        string api = Path.Combine(root, "api");
                        Directory.CreateDirectory(Path.Combine(api, ".git"));
                        Write(api, "pom.xml", "<project><artifactId>junit-jupiter</artifactId></project>");
                        ToolResult javaCreated = await RunAsync(skills, "java-new-class", "create", new List<string> { "com.example.Orders" }, api, false, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual(0, ReadExit(javaCreated), "class created: " + javaCreated.Content);
                        string orders = Path.Combine(api, "src", "main", "java", "com", "example", "Orders.java");
                        string ordersTest = Path.Combine(api, "src", "test", "java", "com", "example", "OrdersTest.java");
                        MuxAssert.Contains("package com.example;", File.ReadAllText(orders), "package line");
                        MuxAssert.Contains("org.junit.jupiter.api.Test", File.ReadAllText(ordersTest), "JUnit 5 test");
                    }).ConfigureAwait(false);
                }, skip: !pwsh, skipReason: "pwsh is not on PATH")            };

            List<ToolchainCase> all = new List<ToolchainCase>(Cases());
            all.AddRange(MoreCases());
            all.AddRange(InfrastructureCases());
            foreach (ToolchainCase toolchainCase in all)
            {
                ToolchainCase captured = toolchainCase;
                cases.Add(new TestCaseDescriptor(SuiteId, captured.Id, captured.Name, (CancellationToken ct) => RunCaseAsync(captured, ct), skip: !pwsh, skipReason: "pwsh is not on PATH"));
            }

            return new TestSuiteDescriptor(SuiteId, "Toolchain and infrastructure skills in dry-run mode", cases);
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

        private static IEnumerable<ToolchainCase> MoreCases()
        {
            const string ReactPackage = "{\"dependencies\":{\"react\":\"18.3.1\",\"react-dom\":\"18.3.1\"},\"devDependencies\":{\"vitest\":\"2\",\"@testing-library/react\":\"16\",\"eslint-plugin-react-hooks\":\"5\"}}";

            // --- React ---
            yield return Js("ReactRequiresReact", "React skills exit 2 outside React projects", "{}", "package-lock.json", "react-test", "component", "Button", "not a React project", 2);
            yield return new ToolchainCase
            {
                Id = "ReactComponentDryRun",
                Name = "react-new-component plans a .tsx component and its test",
                Fixture = (string dir) => { Write(dir, "package.json", ReactPackage); Write(dir, "pnpm-lock.yaml", string.Empty); Write(dir, "tsconfig.json", "{}"); Write(Path.Combine(dir, "src"), "main.tsx", string.Empty); },
                Skill = "react-new-component",
                Command = "create",
                Arguments = new List<string> { "Button" },
                Expected = new List<string> { "DRYRUN: create", "Button.tsx", "Button.test.tsx" }
            };
            yield return Js("ReactComponentNameChecked", "Component names must be PascalCase", ReactPackage, "pnpm-lock.yaml", "react-new-component", "create", "button", "PascalCase", 2);
            yield return Js("ReactTestComponent", "react-test filters the runner by component name", ReactPackage, "pnpm-lock.yaml", "react-test", "component", "Button", "DRYRUN: pnpm exec vitest run Button");
            yield return Js("ReactLintHooks", "react-lint-hooks runs ESLint in unix format", ReactPackage, "pnpm-lock.yaml", "react-lint-hooks", "check", null, "DRYRUN: pnpm exec eslint . --format unix");
            yield return Js("ReactUpgradeCheck", "react-upgrade-check reports declared versions and lists installs", ReactPackage, "pnpm-lock.yaml", "react-upgrade-check", "report", null, "declared react 18.3.1");

            // --- Java ---
            yield return new ToolchainCase
            {
                Id = "MavenWrapperWins",
                Name = "The Maven wrapper is preferred over mvn",
                Fixture = (string dir) => { Write(dir, "pom.xml", "<project/>"); Write(dir, "mvnw", string.Empty); Write(dir, "mvnw.cmd", string.Empty); },
                Skill = "java-test",
                Command = "all",
                Expected = new List<string> { "mvnw", "-B test" }
            };
            yield return new ToolchainCase
            {
                Id = "GradleWrapperFilter",
                Name = "Gradle filters use --tests through the wrapper",
                Fixture = (string dir) => { Write(dir, "build.gradle.kts", "plugins { java }"); Write(dir, "gradlew", string.Empty); Write(dir, "gradlew.bat", string.Empty); },
                Skill = "java-test",
                Command = "filter",
                Arguments = new List<string> { "com.example.FooTest" },
                Expected = new List<string> { "gradlew", "--console=plain test --tests com.example.FooTest" }
            };
            yield return Java("MavenPackage", "Maven package skips tests", "pom.xml", "<project/>", "java-build", "package", null, "DRYRUN: mvn -B -q package -DskipTests");
            yield return Java("SpotlessRequired", "java-format needs Spotless", "pom.xml", "<project/>", "java-format", "apply", null, "Spotless is not configured", 2);
            yield return Java("GradleSpotless", "Gradle Spotless applies with spotlessApply", "build.gradle.kts", "plugins { id(\"com.diffplug.spotless\") }", "java-format", "apply", null, "DRYRUN: gradle --console=plain spotlessApply");
            yield return Java("MavenCheckstyle", "Configured Checkstyle runs checkstyle:check", "pom.xml", "<project><artifactId>maven-checkstyle-plugin</artifactId></project>", "java-lint", "check", null, "checkstyle:check");
            yield return Java("JavaNewClassDryRun", "java-new-class plans the class and a JUnit 5 test", "pom.xml", "<project><artifactId>junit-jupiter</artifactId></project>", "java-new-class", "create", "com.example.UserService", "UserServiceTest.java");
            yield return new ToolchainCase
            {
                Id = "NotAJavaProject",
                Name = "A directory without a build file exits 2",
                Fixture = (string dir) => Write(dir, "README.md", "hi"),
                Skill = "java-build",
                Command = "compile",
                Expected = new List<string> { "not a Maven or Gradle project" },
                ExpectedExit = 2
            };

            // --- C and C++ ---
            yield return Cpp("CMakeConfigureDebug", "CMake debug configure exports compile commands", "CMakeLists.txt", "cpp-configure", "debug", null, "DRYRUN: cmake -S . -B build/debug -DCMAKE_BUILD_TYPE=Debug -DCMAKE_EXPORT_COMPILE_COMMANDS=ON");
            yield return Cpp("CMakeBuild", "CMake builds in parallel", "CMakeLists.txt", "cpp-build", "build", null, "--parallel");
            yield return new ToolchainCase
            {
                Id = "CMakePreset",
                Name = "Presets configure with cmake --preset",
                Fixture = (string dir) => { Write(dir, "CMakeLists.txt", "project(x)"); Write(dir, "CMakePresets.json", "{}"); },
                Skill = "cpp-configure",
                Command = "preset",
                Arguments = new List<string> { "dev" },
                Expected = new List<string> { "DRYRUN: cmake --preset dev" }
            };
            yield return Cpp("CTestFilter", "cpp-test filter passes -R to CTest", "CMakeLists.txt", "cpp-test", "filter", "parser", "DRYRUN: ctest --test-dir build/debug --output-on-failure -R parser");
            yield return Cpp("MesonSetup", "Meson projects configure with meson setup", "meson.build", "cpp-configure", "debug", null, "DRYRUN: meson setup build/debug --buildtype=debug");
            yield return Cpp("MakeBuild", "Makefile projects build with make -j", "Makefile", "cpp-build", "build", null, "DRYRUN: make -j");
            yield return new ToolchainCase
            {
                Id = "ClangFormatVerify",
                Name = "cpp-format verify runs clang-format --dry-run --Werror on sources",
                Fixture = (string dir) => { Write(dir, "CMakeLists.txt", "project(x)"); Write(Path.Combine(dir, "src"), "main.cpp", "int main() {}"); },
                Skill = "cpp-format",
                Command = "verify",
                Expected = new List<string> { "DRYRUN: clang-format --dry-run --Werror", "main.cpp" }
            };
            yield return Cpp("AddressSanitizer", "cpp-sanitize asan uses its own tree and flags", "CMakeLists.txt", "cpp-sanitize", "asan", null, "-fsanitize=address");

            // --- Go and Rust ---
            yield return Simple("GoTestFilter", "go-test filter passes -run", "go.mod", "module x", "go-test", "filter", "TestParse", "DRYRUN: go test ./... -run TestParse");
            yield return new ToolchainCase
            {
                Id = "GolangciWhenConfigured",
                Name = "go-lint uses golangci-lint when configured",
                Fixture = (string dir) => { Write(dir, "go.mod", "module x"); Write(dir, ".golangci.yml", "run: {}"); },
                Skill = "go-lint",
                Command = "check",
                Expected = new List<string> { "DRYRUN: golangci-lint run" }
            };
            yield return Simple("GoModTidy", "go-mod tidy runs go mod tidy", "go.mod", "module x", "go-mod", "tidy", null, "DRYRUN: go mod tidy");
            yield return Simple("ClippyCheck", "cargo-clippy check denies warnings", "Cargo.toml", "[package]", "cargo-clippy", "check", null, "DRYRUN: cargo clippy --all-targets -- -D warnings");
            yield return Simple("CargoFmtVerify", "cargo-fmt verify runs cargo fmt --check", "Cargo.toml", "[package]", "cargo-fmt", "verify", null, "DRYRUN: cargo fmt --check");
            yield return Simple("NotARustCrate", "A directory without Cargo.toml exits 2", "README.md", "hi", "cargo-test", "all", null, "not a Rust crate", 2);
        }

        private static IEnumerable<ToolchainCase> InfrastructureCases()
        {
            Dictionary<string, string> Target(string name) => new Dictionary<string, string> { ["MUX_SKILL_DRY_RUN_TARGET"] = name };
            Action<string> k8sFixture = (string dir) => Write(Path.Combine(dir, "k8s"), "app.yaml", "kind: Deployment");

            // --- Kubernetes and Helm ---
            yield return new ToolchainCase { Id = "K8sApplyRefusesProduction", Name = "k8s-apply refuses a production context", Fixture = k8sFixture, Skill = "k8s-apply", Command = "apply", Arguments = new List<string> { "k8s" }, Environment = Target("prod-east"), Expected = new List<string> { "Kubernetes context: prod-east", "refused" }, Unexpected = new List<string> { "DRYRUN: kubectl apply" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "K8sApplyConfirmed", Name = "k8s-apply proceeds when the context is confirmed", Fixture = k8sFixture, Skill = "k8s-apply", Command = "apply", Arguments = new List<string> { "k8s", "--confirm", "prod-east" }, Environment = Target("prod-east"), Expected = new List<string> { "DRYRUN: kubectl apply -f k8s --recursive" } };
            yield return new ToolchainCase { Id = "K8sWrongConfirmRefused", Name = "Confirming a different name does not pass the guard", Fixture = k8sFixture, Skill = "k8s-apply", Command = "apply", Arguments = new List<string> { "k8s", "--confirm", "prod-west" }, Environment = Target("prod-east"), Expected = new List<string> { "refused" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "K8sKustomizeUsesK", Name = "A kustomization directory is applied with -k", Fixture = (string dir) => Write(Path.Combine(dir, "overlays", "dev"), "kustomization.yaml", "resources: []"), Skill = "k8s-apply", Command = "diff", Arguments = new List<string> { "overlays/dev" }, Expected = new List<string> { "DRYRUN: kubectl diff -k overlays/dev" } };
            yield return new ToolchainCase { Id = "K8sValidateServer", Name = "k8s-validate server runs a server-side dry run", Fixture = k8sFixture, Skill = "k8s-validate", Command = "server", Arguments = new List<string> { "k8s" }, Expected = new List<string> { "DRYRUN: kubectl apply --dry-run=server -f k8s --recursive" } };
            yield return new ToolchainCase { Id = "HelmUpgradeAtomic", Name = "helm upgrade installs atomically and waits", Fixture = k8sFixture, Skill = "helm", Command = "upgrade", Arguments = new List<string> { "web", "./chart", "values.yaml" }, Expected = new List<string> { "DRYRUN: helm upgrade web ./chart --install --atomic --wait --timeout 5m -f values.yaml" } };
            yield return new ToolchainCase { Id = "HelmUpgradeGuarded", Name = "helm upgrade refuses a production context", Fixture = k8sFixture, Skill = "helm", Command = "upgrade", Arguments = new List<string> { "web", "./chart" }, Environment = Target("live-cluster"), Expected = new List<string> { "refused" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "MinikubeStartOptions", Name = "minikube start passes the driver and version", Fixture = k8sFixture, Skill = "minikube", Command = "start", Arguments = new List<string> { "docker", "v1.30.0" }, Expected = new List<string> { "DRYRUN: minikube start --driver=docker --kubernetes-version=v1.30.0" } };
            yield return new ToolchainCase { Id = "ProdPatternConfigurable", Name = "A custom production pattern changes what the guard matches", Fixture = k8sFixture, Skill = "k8s-apply", Command = "apply", Arguments = new List<string> { "k8s" }, Environment = new Dictionary<string, string> { ["MUX_SKILL_DRY_RUN_TARGET"] = "prod-east", ["MUX_SKILL_PROD_PATTERN"] = "^prd-" }, Expected = new List<string> { "DRYRUN: kubectl apply" } };

            // --- Docker and Compose ---
            yield return new ToolchainCase { Id = "DockerBuildDefaultTag", Name = "docker-build tags with the folder name", Fixture = (string dir) => Write(Path.Combine(dir, "api"), "Dockerfile", "FROM scratch"), RunIn = "api", Skill = "docker-build", Command = "build", Expected = new List<string> { "DRYRUN: docker build -t api:dev" } };
            yield return new ToolchainCase { Id = "DockerPushGuarded", Name = "Pushing a production-named image needs confirmation", Fixture = (string dir) => Write(dir, "Dockerfile", "FROM scratch"), Skill = "docker-build", Command = "push", Arguments = new List<string> { "registry.example.com/app:prod" }, Expected = new List<string> { "refused" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "ComposeDownKeepsVolumes", Name = "compose down never removes volumes", Fixture = (string dir) => Write(dir, "compose.yaml", "services: {}"), Skill = "compose", Command = "down", Expected = new List<string> { "compose -f", " down" }, Unexpected = new List<string> { " -v", "--volumes" } };
            yield return new ToolchainCase { Id = "ComposeLogsBounded", Name = "compose logs are bounded", Fixture = (string dir) => Write(dir, "compose.yaml", "services: {}"), Skill = "compose", Command = "logs", Arguments = new List<string> { "web", "50" }, Expected = new List<string> { "logs --no-color --tail 50 web" } };
            if (!IsOnPath("hadolint"))
            {
                yield return new ToolchainCase { Id = "DockerfileChecklist", Name = "dockerfile-lint flags unpinned images and root users", Fixture = (string dir) => Write(dir, "Dockerfile", "FROM node\nCMD [\"node\", \"app.js\"]\n"), Skill = "dockerfile-lint", Command = "check", Expected = new List<string> { "not pinned", "No USER", "No HEALTHCHECK" }, ExpectedExit = 1 };
            }

            // --- Clouds ---
            yield return new ToolchainCase { Id = "S3SyncPreviewsByDefault", Name = "s3-sync is a dry run unless apply is passed", Fixture = k8sFixture, Skill = "aws-storage", Command = "s3-sync", Arguments = new List<string> { "./dist", "s3://bucket/site" }, Expected = new List<string> { "AWS profile: dev-local", "DRYRUN: aws s3 sync ./dist s3://bucket/site --dryrun" }, Unexpected = new List<string> { "--delete" } };
            yield return new ToolchainCase { Id = "S3SyncApplyGuarded", Name = "s3-sync apply refuses a production profile", Fixture = k8sFixture, Skill = "aws-storage", Command = "s3-sync", Arguments = new List<string> { "./dist", "s3://bucket/site", "apply" }, Environment = Target("prod-admin"), Expected = new List<string> { "refused" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "Ec2StopConfirmed", Name = "ec2-stop runs on a confirmed production profile", Fixture = k8sFixture, Skill = "aws-compute", Command = "ec2-stop", Arguments = new List<string> { "i-0abc", "--confirm", "production" }, Environment = Target("production"), Expected = new List<string> { "DRYRUN: aws ec2 stop-instances --instance-ids i-0abc --output table" } };
            yield return new ToolchainCase { Id = "CdkDeployPreviewsByDefault", Name = "cdk-deploy shows a diff unless apply is passed", Fixture = k8sFixture, Skill = "aws-deploy", Command = "cdk-deploy", Expected = new List<string> { "Preview only", "DRYRUN: cdk diff" }, Unexpected = new List<string> { "cdk deploy" } };
            yield return new ToolchainCase { Id = "SecretsListNamesOnly", Name = "secrets-list asks for names, never values", Fixture = k8sFixture, Skill = "aws-integration", Command = "secrets-list", Expected = new List<string> { "list-secrets", "SecretList[].[Name,LastChangedDate]" }, Unexpected = new List<string> { "get-secret-value" } };
            yield return new ToolchainCase { Id = "AzureDeallocateConfirmed", Name = "vm-deallocate runs when confirmed", Fixture = k8sFixture, Skill = "azure-compute", Command = "vm-deallocate", Arguments = new List<string> { "rg", "vm1", "--confirm", "Prod Subscription" }, Environment = Target("Prod Subscription"), Expected = new List<string> { "Azure subscription: Prod Subscription", "DRYRUN: az vm deallocate --resource-group rg --name vm1" } };
            yield return new ToolchainCase { Id = "CloudRunNoTraffic", Name = "Cloud Run deploys go out with no traffic", Fixture = k8sFixture, Skill = "gcp-run", Command = "run-deploy", Arguments = new List<string> { "api", "gcr.io/x/api:1", "us-central1" }, Expected = new List<string> { "--no-traffic", "--region us-central1" } };
            yield return new ToolchainCase { Id = "VercelProdNeedsConfirm", Name = "Vercel production deploys always need confirmation", Fixture = (string dir) => Write(dir, "vercel.json", "{}"), Skill = "vercel", Command = "deploy-prod", Expected = new List<string> { "refused" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "VercelProdConfirmed", Name = "Vercel production deploys run when confirmed", Fixture = (string dir) => Write(dir, "vercel.json", "{}"), Skill = "vercel", Command = "deploy-prod", Arguments = new List<string> { "--confirm", "production" }, Expected = new List<string> { "DRYRUN: vercel deploy --prod --yes" } };
            yield return new ToolchainCase { Id = "CloudflareStagingDeploy", Name = "Cloudflare deploys to a named environment without the guard", Fixture = (string dir) => Write(dir, "wrangler.toml", "name = \"w\""), Skill = "cloudflare", Command = "deploy", Arguments = new List<string> { "staging" }, Expected = new List<string> { "DRYRUN: wrangler deploy --env staging" } };
            yield return new ToolchainCase { Id = "HeatPreviewIsDryRun", Name = "openstack-heat preview is a dry run", Fixture = (string dir) => Write(dir, "clouds.yaml", "clouds: {}"), Skill = "openstack-heat", Command = "preview", Arguments = new List<string> { "web", "stack.hot.yaml" }, Expected = new List<string> { "DRYRUN: openstack stack create --dry-run -t stack.hot.yaml web" } };

            // --- Infrastructure as code ---
            yield return new ToolchainCase { Id = "TerraformPlanSaves", Name = "terraform plan writes mux.tfplan", Fixture = (string dir) => Write(dir, "main.tf", "terraform {}"), Skill = "terraform", Command = "plan", Expected = new List<string> { "plan -input=false -out=mux.tfplan" } };
            yield return new ToolchainCase { Id = "TerraformApplyGuarded", Name = "terraform apply refuses a production workspace", Fixture = (string dir) => Write(dir, "main.tf", "terraform {}"), Skill = "terraform", Command = "apply", Environment = Target("prod"), Expected = new List<string> { "Terraform workspace: prod", "refused" }, ExpectedExit = 3 };
            yield return new ToolchainCase { Id = "TerraformApplySavedPlan", Name = "terraform apply applies only the saved plan", Fixture = (string dir) => Write(dir, "main.tf", "terraform {}"), Skill = "terraform", Command = "apply", Expected = new List<string> { "apply -input=false mux.tfplan" }, Unexpected = new List<string> { "-auto-approve" } };
            yield return new ToolchainCase { Id = "PulumiPreview", Name = "pulumi preview shows a diff non-interactively", Fixture = (string dir) => Write(dir, "Pulumi.yaml", "name: x"), Skill = "pulumi", Command = "preview", Expected = new List<string> { "DRYRUN: pulumi preview --diff --non-interactive" } };
        }

        private static ToolchainCase Java(string id, string name, string file, string content, string skill, string command, string? argument, string expected, int exit = 0)
        {
            return Simple(id, name, file, content, skill, command, argument, expected, exit);
        }

        private static ToolchainCase Cpp(string id, string name, string file, string skill, string command, string? argument, string expected)
        {
            return Simple(id, name, file, "project(x)", skill, command, argument, expected);
        }

        private static ToolchainCase Simple(string id, string name, string file, string content, string skill, string command, string? argument, string expected, int exit = 0)
        {
            return new ToolchainCase
            {
                Id = id,
                Name = name,
                Fixture = (string dir) => Write(dir, file, content),
                Skill = skill,
                Command = command,
                Arguments = argument == null ? new List<string>() : new List<string> { argument },
                Expected = new List<string> { expected },
                ExpectedExit = exit
            };
        }

        private static ToolchainCase Js(string id, string name, string packageJson, string lockfile, string skill, string command, string? argument, string expected, int exit = 0)
        {
            return new ToolchainCase
            {
                Id = id,
                Name = name,
                Fixture = (string dir) => { Write(dir, "package.json", packageJson); Write(dir, lockfile, string.Empty); },
                Skill = skill,
                Command = command,
                Arguments = argument == null ? new List<string>() : new List<string> { argument },
                Expected = new List<string> { expected },
                ExpectedExit = exit
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

                ToolResult result = await RunAsync(skills, toolchainCase.Skill, toolchainCase.Command, toolchainCase.Arguments, runIn, true, ct, toolchainCase.Environment).ConfigureAwait(false);
                string stdout = ReadField(result, "stdout");
                MuxAssert.AreEqual(toolchainCase.ExpectedExit, ReadExit(result), "exit code: " + result.Content);
                foreach (string expected in toolchainCase.Expected)
                {
                    MuxAssert.Contains(expected, stdout, "output of " + toolchainCase.Skill + " " + toolchainCase.Command);
                }

                foreach (string unexpected in toolchainCase.Unexpected)
                {
                    MuxAssert.DoesNotContain(unexpected, stdout, "output of " + toolchainCase.Skill + " " + toolchainCase.Command);
                }
            });
        }

        private static async Task<ToolResult> RunAsync(string skillsDirectory, string skillId, string command, List<string> arguments, string workingDirectory, bool dryRun, CancellationToken ct, Dictionary<string, string>? extraEnvironment = null)
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

            environment["MUX_SKILL_PROD_PATTERN"] = MuxSettings.DefaultSkillProdPattern;
            environment["MUX_SKILL_DRY_RUN_TARGET"] = "dev-local";
            if (extraEnvironment != null)
            {
                foreach (KeyValuePair<string, string> variable in extraEnvironment)
                {
                    environment[variable.Key] = variable.Value;
                }
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
