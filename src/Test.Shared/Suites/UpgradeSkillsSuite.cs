namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the version upgrade skills (dotnet-upgrade, node-upgrade, py-upgrade): plans that change
    /// nothing, applies that change exactly the planned text, notes for what is left to the user, encodings and line
    /// endings preserved, and bad input. Positive and negative cases.
    /// </summary>
    public static class UpgradeSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "UpgradeSkills";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready, skipReason: "pwsh is not on PATH"));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "The three upgrade skills are mutating languages skills with plan and apply", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                foreach (string id in new[] { "dotnet-upgrade", "node-upgrade", "py-upgrade" })
                {
                    MuxAssert.IsTrue(defs[id].Mutating, id + " mutating");
                    MuxAssert.AreEqual("languages", DefaultSkillCategories.For(id), id + " category");
                    MuxAssert.AreEqual("plan,apply", string.Join(",", defs[id].Commands.Select(c => c.Name)), id + " commands");
                }

                return Task.CompletedTask;
            }));

            Add("DotnetPlanThenApply", "dotnet-upgrade plans without touching files, then applies exactly the planned edits and skips build output", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net8.0</TargetFramework>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include=\"Microsoft.EntityFrameworkCore\" Version=\"8.0.4\" />\n  </ItemGroup>\n</Project>\n");
                Write(c, "src/Lib/Lib.csproj", "<Project>\n  <PropertyGroup>\n    <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>\n  </PropertyGroup>\n</Project>\n");
                Write(c, "src/App/bin/Debug/Copy.csproj", "<Project><PropertyGroup><TargetFramework>net6.0</TargetFramework></PropertyGroup></Project>");
                Write(c, "global.json", "{ \"sdk\": { \"version\": \"99.0.100\" } }");
                string before = Fingerprint(c.Project);
                (await c.Run("dotnet-upgrade", "plan", "net10.0").ConfigureAwait(false))
                    .Exit(0).Has("Changes to target net10.0 (2 files)").Has("src/App/App.csproj: TargetFramework net8.0 -> net10.0")
                    .Has("src/Lib/Lib.csproj: TargetFrameworks net8.0;netstandard2.0 -> adds net10.0").Has("Microsoft.EntityFrameworkCore 8.x").Lacks("Copy.csproj").Lacks("global.json:").Has("Nothing was changed");
                MuxAssert.AreEqual(before, Fingerprint(c.Project), "plan changed nothing");
                (await c.Run("dotnet-upgrade", "apply", "net10.0", "--no-build").ConfigureAwait(false)).Exit(0).Has("Updated src/App/App.csproj").Has("Updated src/Lib/Lib.csproj");
                MuxAssert.Contains("<TargetFramework>net10.0</TargetFramework>", Read(c, "src/App/App.csproj"), "single target replaced");
                MuxAssert.Contains("Version=\"8.0.4\"", Read(c, "src/App/App.csproj"), "packages left alone");
                MuxAssert.Contains("<TargetFrameworks>net8.0;netstandard2.0;net10.0</TargetFrameworks>", Read(c, "src/Lib/Lib.csproj"), "multi-target gains the new framework");
                MuxAssert.Contains("net6.0", Read(c, "src/App/bin/Debug/Copy.csproj"), "build output untouched");
                (await c.Run("dotnet-upgrade", "plan", "net10.0").ConfigureAwait(false)).Exit(0).Has("Nothing to change");
            });
            Add("DotnetApplyDryRunBuilds", "A dry-run apply reports the writes and the build without changing anything", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "App.sln", "");
                Write(c, "App/App.csproj", "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
                string before = Fingerprint(c.Project);
                (await c.Run(true, "dotnet-upgrade", "apply", "net10.0").ConfigureAwait(false)).Exit(0).Has("DRYRUN: would write App/App.csproj").Has("DRYRUN: dotnet build App.sln --nologo");
                MuxAssert.AreEqual(before, Fingerprint(c.Project), "dry run changed nothing");
            });
            Add("EncodingAndLineEndingsKept", "apply keeps a UTF-8 BOM and CRLF line endings", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                string path = Path.Combine(c.Project, "App.csproj");
                File.WriteAllText(path, "<Project>\r\n  <PropertyGroup>\r\n    <TargetFramework>net8.0</TargetFramework>\r\n  </PropertyGroup>\r\n</Project>\r\n", new UTF8Encoding(true));
                (await c.Run("dotnet-upgrade", "apply", "net10.0", "--no-build").ConfigureAwait(false)).Exit(0);
                byte[] bytes = File.ReadAllBytes(path);
                MuxAssert.IsTrue(bytes.Length > 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM kept");
                string text = File.ReadAllText(path);
                MuxAssert.Contains("<TargetFramework>net10.0</TargetFramework>\r\n", text, "CRLF kept");
                MuxAssert.AreEqual(5, text.Split("\r\n").Length - 1, "every line still ends in CRLF");
            });
            Add("NodePlanThenApply", "node-upgrade updates .nvmrc, engines, workflows, and Dockerfiles, and notes matrices and @types/node", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, ".nvmrc", "18\n");
                Write(c, "package.json", "{\n  \"name\": \"x\",\n  \"engines\": { \"node\": \">=18\" },\n  \"devDependencies\": { \"@types/node\": \"^18.11.0\" }\n}\n");
                Write(c, ".github/workflows/ci.yml", "jobs:\n  b:\n    steps:\n      - uses: actions/setup-node@v4\n        with:\n          node-version: 18.x\n  m:\n    strategy:\n      matrix:\n        node-version: [18, 20]\n");
                Write(c, "Dockerfile", "FROM node:18-alpine AS web\nRUN npm ci\n");
                string before = Fingerprint(c.Project);
                (await c.Run("node-upgrade", "plan", "v22").ConfigureAwait(false))
                    .Exit(0).Has(".nvmrc: 18 -> 22").Has("engines.node >=18 -> >=22").Has("node-version 18.x -> 22.x").Has("FROM node:18-alpine -> node:22-alpine")
                    .Has("@types/node is 18.x").Has("has a node-version matrix");
                MuxAssert.AreEqual(before, Fingerprint(c.Project), "plan changed nothing");
                (await c.Run("node-upgrade", "apply", "22").ConfigureAwait(false)).Exit(0);
                MuxAssert.AreEqual("22\n", Read(c, ".nvmrc"), ".nvmrc");
                MuxAssert.Contains("\"node\": \">=22\"", Read(c, "package.json"), "engines");
                MuxAssert.Contains("\"@types/node\": \"^18.11.0\"", Read(c, "package.json"), "@types/node left to the package manager");
                MuxAssert.Contains("node-version: 22.x", Read(c, ".github/workflows/ci.yml"), "workflow");
                MuxAssert.Contains("node-version: [18, 20]", Read(c, ".github/workflows/ci.yml"), "matrix untouched");
                MuxAssert.Contains("FROM node:22-alpine AS web", Read(c, "Dockerfile"), "Dockerfile");
            });
            Add("PythonPlanThenApply", "py-upgrade updates requires-python, tool targets, .python-version, workflows, and Dockerfiles, and notes classifiers", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "pyproject.toml", "[project]\nrequires-python = \">=3.9\"\nclassifiers = [\"Programming Language :: Python :: 3.9\"]\n[tool.ruff]\ntarget-version = \"py39\"\n[tool.mypy]\npython_version = \"3.9\"\n");
                Write(c, ".python-version", "3.9.18\n");
                Write(c, ".github/workflows/ci.yml", "jobs:\n  b:\n    steps:\n      - uses: actions/setup-python@v5\n        with:\n          python-version: \"3.9\"\n");
                Write(c, "Dockerfile", "FROM python:3.9-slim\n");
                string before = Fingerprint(c.Project);
                (await c.Run("py-upgrade", "plan", "3.12").ConfigureAwait(false))
                    .Exit(0).Has("requires-python >=3.9 -> >=3.12").Has("target-version py39 -> py312").Has("mypy python_version 3.9 -> 3.12")
                    .Has(".python-version: 3.9.18 -> 3.12").Has("python-version 3.9 -> 3.12").Has("FROM python:3.9-slim -> python:3.12-slim").Has("classifiers");
                MuxAssert.AreEqual(before, Fingerprint(c.Project), "plan changed nothing");
                (await c.Run("py-upgrade", "apply", "3.12").ConfigureAwait(false)).Exit(0);
                string pyproject = Read(c, "pyproject.toml");
                MuxAssert.Contains("requires-python = \">=3.12\"", pyproject, "requires-python");
                MuxAssert.Contains("target-version = \"py312\"", pyproject, "ruff");
                MuxAssert.Contains("python_version = \"3.12\"", pyproject, "mypy");
                MuxAssert.Contains("Python :: 3.9", pyproject, "classifiers left to the user");
                MuxAssert.Contains("python-version: \"3.12\"", Read(c, ".github/workflows/ci.yml"), "quotes kept");
                MuxAssert.AreEqual("FROM python:3.12-slim\n", Read(c, "Dockerfile"), "Dockerfile");
            });
            Add("BadInputAndNoProject", "Bad versions and projects with nothing to upgrade exit 2 with the reason", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "README.md", "x");
                (await c.Run("dotnet-upgrade", "plan", "10").ConfigureAwait(false)).Exit(2).Has("for example net10.0");
                (await c.Run("dotnet-upgrade", "plan", "net10.0").ConfigureAwait(false)).Exit(2).Has("no .NET project files");
                (await c.Run("node-upgrade", "plan", "latest").ConfigureAwait(false)).Exit(2).Has("for example 22");
                (await c.Run("node-upgrade", "plan", "22").ConfigureAwait(false)).Exit(2).Has("no Node.js version pins");
                (await c.Run("py-upgrade", "plan", "2.7").ConfigureAwait(false)).Exit(2).Has("for example 3.12");
                (await c.Run("py-upgrade", "plan", "3.12").ConfigureAwait(false)).Exit(2).Has("no Python version pins");
            });

            return new TestSuiteDescriptor(SuiteId, "Version upgrade skills: .NET, Node.js, Python", cases);
        }

        #endregion

        #region Private-Methods

        private static string Fingerprint(string root)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                builder.Append(Path.GetRelativePath(root, file)).Append(':').Append(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))).Append('\n');
            }

            return builder.ToString();
        }

        private static string Read(SkillTestContext c, string relative)
        {
            return File.ReadAllText(Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static string Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static bool IsOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string candidate in new[] { executable, executable + ".exe", executable + ".cmd" })
                {
                    if (File.Exists(Path.Combine(directory, candidate))) return true;
                }
            }

            return false;
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-upgrades-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                Directory.CreateDirectory(Path.Combine(root, "project"));
                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
