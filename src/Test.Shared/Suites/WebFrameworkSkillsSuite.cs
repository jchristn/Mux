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
    /// Touchstone suite for the web-framework and storybook skills: framework detection, scripts preferred over
    /// framework tools, per-package-manager commands, dev-server descriptions, version comparison, and refusals.
    /// Positive and negative cases.
    /// </summary>
    public static class WebFrameworkSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "WebFrameworkSkills";

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

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "web-framework and storybook are read-only frontend skills", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                MuxAssert.AreEqual("detect,build,lint,test,dev,upgrade-check", string.Join(",", defs["web-framework"].Commands.Select(c => c.Name)), "web-framework commands");
                MuxAssert.AreEqual("build,test,dev", string.Join(",", defs["storybook"].Commands.Select(c => c.Name)), "storybook commands");
                MuxAssert.AreEqual("frontend", DefaultSkillCategories.For("web-framework"), "category");
                MuxAssert.AreEqual(".storybook/main.*", string.Join(",", defs["storybook"].AppliesTo), "storybook gate");
                return Task.CompletedTask;
            }));

            Add("NextUsesScriptsThenTools", "Next.js: detect, the framework tools without scripts, the dev script's port, and declared versus installed versions", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "package.json", "{\"dependencies\":{\"next\":\"15.1.0\",\"react\":\"19.0.0\"},\"scripts\":{\"dev\":\"next dev -p 3100\"}}");
                Write(c, "package-lock.json", "{}");
                Write(c, "node_modules/next/package.json", "{\"version\":\"15.0.3\"}");
                (await c.Run("web-framework", "detect").ConfigureAwait(false)).Exit(0).Has("Framework: Next.js (next 15.1.0)").Has("Package manager: npm").Has("Script dev: next dev -p 3100");
                (await c.Run(true, "web-framework", "build").ConfigureAwait(false)).Exit(0).Has("DRYRUN: npx --no-install next build");
                (await c.Run(true, "web-framework", "lint").ConfigureAwait(false)).Exit(0).Has("next lint");
                (await c.Run(true, "web-framework", "test").ConfigureAwait(false)).Exit(2).Has("no test script");
                (await c.Run("web-framework", "dev").ConfigureAwait(false)).Exit(0).Has("Dev command: npm run dev").Has("Expected URL: http://localhost:3100").Has("Ready pattern:");
                (await c.Run("web-framework", "upgrade-check").ConfigureAwait(false)).Exit(0).Has("15.1.0").Has("15.0.3").Has("(not installed)");
                Write(c, "package.json", "{\"dependencies\":{\"next\":\"15.1.0\"},\"scripts\":{\"build\":\"next build --turbo\",\"test\":\"vitest\"}}");
                (await c.Run(true, "web-framework", "build").ConfigureAwait(false)).Exit(0).Has("DRYRUN: npm run build");
                (await c.Run(true, "web-framework", "test").ConfigureAwait(false)).Exit(0).Has("DRYRUN: npm run test");
            });
            Add("AngularWithPnpm", "Angular under pnpm runs ng through pnpm exec, tests once, and serves on 4200", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "package.json", "{\"dependencies\":{\"@angular/core\":\"^19.0.0\"},\"devDependencies\":{\"@angular/cli\":\"^19.0.0\"}}");
                Write(c, "pnpm-lock.yaml", "");
                (await c.Run(true, "web-framework", "build").ConfigureAwait(false)).Exit(0).Has("DRYRUN: pnpm exec ng build");
                (await c.Run(true, "web-framework", "test").ConfigureAwait(false)).Exit(0).Has("ng test --watch=false");
                (await c.Run("web-framework", "dev").ConfigureAwait(false)).Exit(0).Has("Dev command: pnpm exec ng serve").Has("http://localhost:4200");
            });
            Add("SvelteKitFallbacks", "SvelteKit lints with svelte-check and tests with vitest run when there are no scripts", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "package.json", "{\"devDependencies\":{\"@sveltejs/kit\":\"^2.0.0\",\"svelte\":\"^5.0.0\",\"svelte-check\":\"^4\",\"vitest\":\"^2\"}}");
                (await c.Run("web-framework", "detect").ConfigureAwait(false)).Exit(0).Has("Framework: SvelteKit");
                (await c.Run(true, "web-framework", "lint").ConfigureAwait(false)).Exit(0).Has("svelte-check");
                (await c.Run(true, "web-framework", "test").ConfigureAwait(false)).Exit(0).Has("vitest run");
                (await c.Run(true, "web-framework", "build").ConfigureAwait(false)).Exit(0).Has("vite build");
            });
            Add("VueWithoutLinter", "A Vue app without a lint script or linter is refused for lint, and a plain React app is pointed at the react skills", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "package.json", "{\"dependencies\":{\"vue\":\"^3.5.0\"},\"devDependencies\":{\"vite\":\"^6\"}}");
                Write(c, "yarn.lock", "");
                (await c.Run("web-framework", "detect").ConfigureAwait(false)).Exit(0).Has("Framework: Vue").Has("Package manager: yarn");
                (await c.Run(true, "web-framework", "lint").ConfigureAwait(false)).Exit(2).Has("no lint script and no linter for Vue");
                Write(c, "package.json", "{\"dependencies\":{\"react\":\"19.0.0\"}}");
                (await c.Run("web-framework", "detect").ConfigureAwait(false)).Exit(2).Has("use the react-* skills");
            });
            Add("Storybook", "storybook builds and tests through scripts or its tools, describes the dev server, and needs Storybook", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "package.json", "{\"dependencies\":{\"react\":\"19.0.0\"}}");
                (await c.Run(true, "storybook", "build").ConfigureAwait(false)).Exit(2).Has("does not use Storybook");
                Write(c, ".storybook/main.ts", "export default {};");
                Write(c, "package.json", "{\"devDependencies\":{\"storybook\":\"^8\",\"@storybook/test-runner\":\"^0.19\"},\"scripts\":{\"storybook\":\"storybook dev -p 6006\"}}");
                (await c.Run(true, "storybook", "build").ConfigureAwait(false)).Exit(0).Has("DRYRUN: npx --no-install storybook build");
                (await c.Run(true, "storybook", "test").ConfigureAwait(false)).Exit(0).Has("test-storybook");
                (await c.Run("storybook", "dev").ConfigureAwait(false)).Exit(0).Has("Dev command: npm run storybook").Has("http://localhost:6006");
                Write(c, "package.json", "{\"devDependencies\":{\"storybook\":\"^8\"}}");
                (await c.Run(true, "storybook", "test").ConfigureAwait(false)).Exit(2).Has("story tests are not set up");
            });

            return new TestSuiteDescriptor(SuiteId, "Web framework and Storybook skills", cases);
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
            string root = Path.Combine(Path.GetTempPath(), "mux-webfw-" + Guid.NewGuid().ToString("N"));
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
