namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the openapi and openapi-client skills: spec discovery, linting, comparing with a git base,
    /// comparing two files, and client generation into a safe folder. Positive and negative cases.
    /// </summary>
    public static class ApiContractSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "ApiContractSkills";

        private const string Spec = "openapi: 3.0.3\ninfo:\n  title: Shop\n  version: 1.0.0\npaths:\n  /orders:\n    get:\n      responses:\n        '200':\n          description: ok\n";

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
            void Add(string id, string name, Func<SkillTestContext, Task> body, bool skip = false, string reason = "")
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready || skip, skipReason: !ready ? "pwsh is not on PATH" : reason));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "openapi is a read-only review skill and openapi-client a mutating scaffolding skill, both gated on spec files", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                MuxAssert.IsFalse(defs["openapi"].Mutating, "openapi read-only");
                MuxAssert.IsTrue(defs["openapi-client"].Mutating, "openapi-client writes files");
                MuxAssert.AreEqual("review", DefaultSkillCategories.For("openapi"), "openapi category");
                MuxAssert.AreEqual("scaffolding", DefaultSkillCategories.For("openapi-client"), "client category");
                MuxAssert.IsTrue(defs["openapi"].AppliesTo.Contains("**/openapi*.yaml") && defs["openapi"].AppliesTo.Contains("**/swagger*.json"), "gated on specs");
                return Task.CompletedTask;
            }));

            Add("LintFindsTheSpec", "lint finds the spec when no path is given, and refuses a missing one", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                (await c.Run(true, "openapi", "lint").ConfigureAwait(false)).Exit(2).Has("no OpenAPI document found");
                Write(c, "api/openapi.yaml", Spec);
                Write(c, "node_modules/pkg/openapi.yaml", Spec);
                (await c.Run(true, "openapi", "lint").ConfigureAwait(false)).Exit(0).Has("lint api/openapi.yaml").Lacks("node_modules");
                (await c.Run(false, "openapi", "lint", "missing.yaml").ConfigureAwait(false)).Exit(2).Has("OpenAPI document not found");
            });

            Add("DiffAgainstBase", "diff compares the spec with the default branch through oasdiff, and a spec new on the branch cannot break anything", async (SkillTestContext c) =>
            {
                GitFixture repo = c.Repo();
                repo.Write("openapi.yaml", Spec).Write("README.md", "x");
                repo.Commit("initial");
                Write(c, "openapi.yaml", Spec.Replace("/orders", "/purchases"));
                (await c.Run(true, "openapi", "diff").ConfigureAwait(false))
                    .Exit(0).Has("Comparing openapi.yaml with main:openapi.yaml").Has("DRYRUN: oasdiff changelog").Has("--fail-on ERR");
                Write(c, "v2/openapi.yaml", Spec);
                (await c.Run(true, "openapi", "diff", "v2/openapi.yaml", "main").ConfigureAwait(false)).Exit(0).Has("does not exist at main; it is new");
            }, !GitFixture.IsAvailable(), "git is not on PATH");

            Add("BreakingBetweenFiles", "breaking compares two documents and needs both", async (SkillTestContext c) =>
            {
                Write(c, "old.yaml", Spec);
                Write(c, "new.yaml", Spec);
                (await c.Run(true, "openapi", "breaking", "old.yaml", "new.yaml").ConfigureAwait(false)).Exit(0).Has("DRYRUN: oasdiff breaking old.yaml new.yaml --fail-on ERR");
                (await c.Run(true, "openapi", "breaking", "old.yaml").ConfigureAwait(false)).Exit(2).Has("pass both documents");
                (await c.Run(false, "openapi", "breaking", "old.yaml", "gone.yaml").ConfigureAwait(false)).Exit(2).Has("not found: gone.yaml");
            });

            Add("ClientGeneration", "openapi-client generates into a relative folder and refuses paths outside the repository", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "openapi.json", "{\"openapi\":\"3.0.3\",\"info\":{\"title\":\"x\",\"version\":\"1\"},\"paths\":{}}");
                (await c.Run(true, "openapi-client", "generate", "typescript-fetch", "clients/ts").ConfigureAwait(false))
                    .Exit(0).Has("generate -i openapi.json -g typescript-fetch -o clients/ts");
                (await c.Run(true, "openapi-client", "generate", "csharp", "../outside").ConfigureAwait(false)).Exit(2).Has("relative folder inside the repository");
                string rooted = OperatingSystem.IsWindows() ? "C:\\out" : "/tmp/out";
                (await c.Run(true, "openapi-client", "generate", "csharp", rooted).ConfigureAwait(false)).Exit(2).Has("relative folder");
                (await c.Run(true, "openapi-client", "generate", "csharp").ConfigureAwait(false)).Exit(2).Has("pass the generator and output folder");
                (await c.Run(true, "openapi-client", "generators").ConfigureAwait(false)).Exit(0).Has("list --short");
            });

            return new TestSuiteDescriptor(SuiteId, "OpenAPI lint, breaking changes, and client generation", cases);
        }

        #endregion

        #region Private-Methods

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
            string root = Path.Combine(Path.GetTempPath(), "mux-apicontract-" + Guid.NewGuid().ToString("N"));
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
