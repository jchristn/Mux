namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for relevance-gated skill listing: the <c>appliesTo</c> glob matcher, the three
    /// listing modes, the hidden-skills footer, and the settings that drive them.
    /// </summary>
    public static class SkillListingSuite
    {
        private const string SuiteId = "SkillListing";

        /// <summary>
        /// Builds the skill-listing suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the listing cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Relevance-gated skill listing (appliesTo)",
                new List<TestCaseDescriptor>
                {
                    Case("MatcherHandlesLiteralWildcardAndGlobstar", "Literal, wildcard, and ** globs match; escaping globs never do", (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        File.WriteAllText(Path.Combine(f.Project, "package.json"), "{}");
                        File.WriteAllText(Path.Combine(f.Project, "requirements-dev.txt"), "");
                        Directory.CreateDirectory(Path.Combine(f.Project, "src", "deep", "er"));
                        File.WriteAllText(Path.Combine(f.Project, "src", "deep", "er", "Thing.csproj"), "");
                        Directory.CreateDirectory(Path.Combine(f.Project, "node_modules", "pkg"));
                        File.WriteAllText(Path.Combine(f.Project, "node_modules", "pkg", "Hidden.csproj"), "");

                        AppliesToMatcher matcher = new AppliesToMatcher();
                        MuxAssert.IsTrue(matcher.Matches(f.Project, "package.json"), "literal");
                        MuxAssert.IsTrue(matcher.Matches(f.Project, "./requirements*.txt"), "wildcard with ./ prefix");
                        MuxAssert.IsTrue(matcher.Matches(f.Project, "**/*.csproj"), "globstar");
                        MuxAssert.IsTrue(matcher.Matches(f.Project, "src/*/er/Thing.csproj"), "wildcard directory segment");
                        MuxAssert.IsFalse(matcher.Matches(f.Project, "Cargo.toml"), "missing file");
                        MuxAssert.IsFalse(matcher.Matches(f.Project, "**/Hidden.csproj"), "dependency directories skipped");
                        MuxAssert.IsFalse(matcher.Matches(f.Project, "../project/package.json"), "parent escape rejected");
                        MuxAssert.IsFalse(matcher.Matches(f.Project, Path.Combine(f.Project, "package.json")), "rooted glob rejected");
                        MuxAssert.IsTrue(matcher.AnyMatch(f.Project, new List<string>()), "empty list matches");
                        MuxAssert.IsTrue(matcher.AnyMatch(f.Project, new List<string> { "go.mod", "package.json" }), "any of several");
                        return Task.CompletedTask;
                    }),

                    Case("RelevantModeHidesUnmatchedSkills", "Relevant mode lists matching and ungated skills and counts the rest", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        File.WriteAllText(Path.Combine(f.Project, "package.json"), "{}");
                        WriteGated(f.UserSkills, "js-thing", "[package.json]");
                        WriteGated(f.UserSkills, "py-thing", "[pyproject.toml, requirements*.txt]");
                        ProjectSkillsSuite.WritePlaybook(f.UserSkills, "everywhere", "x");

                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            string section = runtime.BuildPromptSection(f.Project);
                            MuxAssert.Contains("- js-thing:", section, "matching skill listed");
                            MuxAssert.Contains("- everywhere:", section, "ungated skill listed");
                            MuxAssert.DoesNotContain("- py-thing:", section, "unmatched skill hidden");
                            MuxAssert.Contains("1 more skills are installed", section, "footer counts hidden skills");
                            MuxAssert.IsTrue(runtime.TryGetSkill("py-thing", f.Project, out _), "hidden skill is still callable");

                            string unfiltered = runtime.BuildPromptSection(null);
                            MuxAssert.Contains("- py-thing:", unfiltered, "no working directory means no filtering");
                        }
                    }),

                    Case("AllAndNoneModes", "All mode lists everything; none mode lists nothing", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WriteGated(f.UserSkills, "py-thing", "[pyproject.toml]");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            runtime.ListingMode = "all";
                            MuxAssert.Contains("- py-thing:", runtime.BuildPromptSection(f.Project), "all lists gated skills");
                            runtime.ListingMode = "none";
                            MuxAssert.AreEqual(string.Empty, runtime.BuildPromptSection(f.Project), "none lists nothing");
                            MuxAssert.AreEqual(2, runtime.GetToolDefinitions().Count, "tools stay available in none mode");
                            runtime.ListingMode = "bogus";
                            MuxAssert.AreEqual("relevant", runtime.ListingMode, "unknown mode falls back to relevant");
                        }
                    }),

                    Case("SettingsNormalizeListingAndRoots", "Listing mode and project roots normalize in settings", (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("relevant", new MuxSettings().SkillListingMode, "default mode");
                        MuxAssert.AreEqual("none", new MuxSettings { SkillListingMode = "OFF" }.SkillListingMode, "off maps to none");
                        MuxAssert.AreEqual("relevant", new MuxSettings { SkillListingMode = "sometimes" }.SkillListingMode, "unknown falls back");
                        MuxAssert.AreEqual(3, new MuxSettings().ProjectSkillRoots.Count, "three default roots");
                        MuxAssert.AreEqual(3, new MuxSettings { ProjectSkillRoots = new List<string>() }.ProjectSkillRoots.Count, "empty restores defaults");
                        List<string> roots = new MuxSettings { ProjectSkillRoots = new List<string> { "a\\b\\", "a/b", "../x", "  " } }.ProjectSkillRoots;
                        MuxAssert.AreEqual(1, roots.Count, "deduplicated and filtered");
                        MuxAssert.AreEqual("a/b", roots[0], "slashes normalized");
                        MuxAssert.IsTrue(new MuxSettings().ProjectSkillsEnabled, "project skills on by default");
                        return Task.CompletedTask;
                    }),

                    Case("RequiresToolsGatesListing", "Skills that require a missing CLI are hidden until it is on PATH", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        string bin = Path.Combine(f.Root, "bin");
                        Directory.CreateDirectory(bin);
                        WriteGatedTools(f.UserSkills, "cloud-thing", "[fakecloud]");
                        WriteGatedTools(f.UserSkills, "either-thing", "[faketf|faketofu]");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            runtime.Tools = new ToolPresenceCache(() => bin) { TimeToLive = TimeSpan.Zero };
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            string hidden = runtime.BuildPromptSection(f.Project);
                            MuxAssert.DoesNotContain("- cloud-thing:", hidden, "hidden without the CLI");
                            MuxAssert.Contains("2 more skills are installed", hidden, "counted in the footer");

                            File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "fakecloud.exe" : "fakecloud"), string.Empty);
                            File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "faketofu.exe" : "faketofu"), string.Empty);
                            string shown = runtime.BuildPromptSection(f.Project);
                            MuxAssert.Contains("- cloud-thing:", shown, "listed once the CLI exists");
                            MuxAssert.Contains("- either-thing:", shown, "an alternative satisfies the requirement");

                            runtime.ListingMode = "all";
                            runtime.Tools = new ToolPresenceCache(() => string.Empty);
                            MuxAssert.Contains("- cloud-thing:", runtime.BuildPromptSection(f.Project), "all mode ignores tool requirements");
                        }
                    }),

                    Case("ToolPresenceCacheRules", "Tool presence handles blanks, alternatives, paths, and caching", (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        string bin = Path.Combine(f.Root, "bin2");
                        Directory.CreateDirectory(bin);
                        ToolPresenceCache cache = new ToolPresenceCache(() => bin) { TimeToLive = TimeSpan.FromMinutes(5) };
                        MuxAssert.IsTrue(cache.IsAvailable("  "), "blank requirement is met");
                        MuxAssert.IsTrue(cache.AllAvailable(null), "null list is met");
                        MuxAssert.IsFalse(cache.IsAvailable("nothere"), "missing tool");
                        File.WriteAllText(Path.Combine(bin, OperatingSystem.IsWindows() ? "nothere.exe" : "nothere"), string.Empty);
                        MuxAssert.IsFalse(cache.IsAvailable("nothere"), "cached answer until the TTL passes");
                        cache.Clear();
                        MuxAssert.IsTrue(cache.IsAvailable("nothere"), "found after clearing");
                        MuxAssert.IsFalse(cache.IsAvailable("../etc/passwd"), "paths are never treated as tools");
                        return Task.CompletedTask;
                    }),

                    Case("RelevanceWithoutProjectSkills", "appliesTo gating works when project skills are disabled", async (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        WriteGated(f.UserSkills, "py-thing", "[pyproject.toml]");
                        using (SkillRuntime runtime = f.CreateRuntime())
                        {
                            runtime.ProjectSkillsEnabled = false;
                            await runtime.RefreshNowAsync(ct).ConfigureAwait(false);
                            MuxAssert.DoesNotContain("- py-thing:", runtime.BuildPromptSection(f.Project), "still filtered by appliesTo");
                        }
                    }),

                    Case("ProdPatternSetting", "skillProdPattern defaults, accepts regexes, and rejects invalid ones", (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("prod|production|live", new MuxSettings().SkillProdPattern, "default");
                        MuxAssert.AreEqual("^prd-", new MuxSettings { SkillProdPattern = " ^prd- " }.SkillProdPattern, "custom pattern trimmed");
                        MuxAssert.AreEqual("prod|production|live", new MuxSettings { SkillProdPattern = "([unclosed" }.SkillProdPattern, "invalid falls back");
                        MuxAssert.AreEqual("prod|production|live", new MuxSettings { SkillProdPattern = "  " }.SkillProdPattern, "blank falls back");
                        return Task.CompletedTask;
                    }),

                    Case("FooterPromptIsEditable", "The hidden-skills footer is a catalog prompt with a {Count} placeholder", (ProjectSkillsFixture f, CancellationToken ct) =>
                    {
                        MuxAssert.Contains("{Count}", PromptResolver.Shared.GetEffective("section.skills.more"), "placeholder in default");
                        MuxAssert.IsFalse(PromptResolver.TryValidateOverride("section.skills.more", "no placeholder here", out _), "override must keep {Count}");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Func<ProjectSkillsFixture, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                ProjectSkillsFixture fixture = new ProjectSkillsFixture();
                try
                {
                    await body(fixture, ct).ConfigureAwait(false);
                }
                finally
                {
                    try { Directory.Delete(fixture.Root, true); } catch (Exception) { }
                }
            });
        }

        private static void WriteGatedTools(string root, string id, string requiresTools)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: " + id + "\ndescription: tool-gated " + id + "\nrequiresTools: " + requiresTools + "\n---\nDo the thing.\n");
        }

        private static void WriteGated(string root, string id, string appliesTo)
        {
            string dir = Path.Combine(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "---\nname: " + id + "\ndescription: gated " + id + "\nappliesTo: " + appliesTo + "\n---\nDo the thing.\n");
        }

        #endregion
    }
}
