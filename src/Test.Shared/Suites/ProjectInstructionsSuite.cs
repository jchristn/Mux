namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Utility;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for project instruction files: precedence within a directory, outer-to-inner ordering
    /// up to the repository root, the user-level file, the size cap, settings gating, and the system-prompt
    /// section that carries them.
    /// </summary>
    public static class ProjectInstructionsSuite
    {
        private const string SuiteId = "ProjectInstructions";

        /// <summary>
        /// Builds the project-instructions suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the instruction-file cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Project instruction files (MUX.md, AGENTS.md, CLAUDE.md)",
                new List<TestCaseDescriptor>
                {
                    Case("FirstNameWinsWithinDirectory", "MUX.md beats AGENTS.md beats CLAUDE.md in one directory", (string root) =>
                    {
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "agents text");
                        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "claude text");

                        ProjectInstructions loaded = new ProjectInstructionsLoader().Load(root);
                        MuxAssert.AreEqual(1, loaded.Sources.Count, "one file per directory");
                        MuxAssert.Contains("agents text", loaded.Text, "AGENTS.md chosen over CLAUDE.md");
                        MuxAssert.DoesNotContain("claude text", loaded.Text, "CLAUDE.md skipped");

                        File.WriteAllText(Path.Combine(root, "MUX.md"), "mux text");
                        loaded = new ProjectInstructionsLoader().Load(root);
                        MuxAssert.Contains("mux text", loaded.Text, "MUX.md chosen first");
                        MuxAssert.DoesNotContain("agents text", loaded.Text, "AGENTS.md skipped when MUX.md exists");
                    }),

                    Case("ClaudeFileAloneIsRead", "A repository set up only for Claude Code is read", (string root) =>
                    {
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "CLAUDE.md"), "use tabs");
                        ProjectInstructions loaded = new ProjectInstructionsLoader().Load(root);
                        MuxAssert.Contains("### CLAUDE.md", loaded.Text, "relative heading");
                        MuxAssert.Contains("use tabs", loaded.Text, "content included");
                    }),

                    Case("OuterBeforeInnerUpToRepoRoot", "Files load from the repo root down to the working directory", (string root) =>
                    {
                        string repo = Path.Combine(root, "repo");
                        string inner = Path.Combine(repo, "src", "app");
                        Directory.CreateDirectory(inner);
                        MakeRepo(repo);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "outside the repo");
                        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "root rules");
                        File.WriteAllText(Path.Combine(inner, "AGENTS.md"), "app rules");

                        ProjectInstructions loaded = new ProjectInstructionsLoader().Load(inner);
                        MuxAssert.AreEqual(2, loaded.Sources.Count, "root and inner only");
                        MuxAssert.IsTrue(loaded.Text.IndexOf("root rules", StringComparison.Ordinal) < loaded.Text.IndexOf("app rules", StringComparison.Ordinal), "outer first");
                        MuxAssert.DoesNotContain("outside the repo", loaded.Text, "stops at the repository root");
                        MuxAssert.Contains("### src/app/AGENTS.md", loaded.Text, "inner heading is root-relative");
                    }),

                    Case("NoRepoReadsOnlyWorkingDirectory", "Outside a repository only the working directory is read", (string root) =>
                    {
                        string child = Path.Combine(root, "child");
                        Directory.CreateDirectory(child);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "parent rules");

                        MuxAssert.IsNull(RepositoryRootLocator.FindRepositoryRoot(child), "fixture is not inside a repository");
                        MuxAssert.IsFalse(new ProjectInstructionsLoader().Load(child).HasContent, "parent not read");

                        File.WriteAllText(Path.Combine(child, "AGENTS.md"), "child rules");
                        MuxAssert.Contains("child rules", new ProjectInstructionsLoader().Load(child).Text, "working directory read");
                    }),

                    Case("GitFileMarksRoot", "A .git file (worktree or submodule) marks the repository root", (string root) =>
                    {
                        string repo = Path.Combine(root, "wt");
                        Directory.CreateDirectory(Path.Combine(repo, "pkg"));
                        File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: /elsewhere");
                        MuxAssert.AreEqual(Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo)), RepositoryRootLocator.FindRepositoryRoot(Path.Combine(repo, "pkg")), "worktree root");
                    }),

                    Case("UserFileComesFirst", "The user-level MUX.md precedes project files", (string root) =>
                    {
                        MakeRepo(root);
                        string user = Path.Combine(root, "user-mux.md");
                        File.WriteAllText(user, "user prefs");
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "project rules");

                        ProjectInstructions loaded = new ProjectInstructionsLoader { UserInstructionsPath = user }.Load(root);
                        MuxAssert.AreEqual(2, loaded.Sources.Count, "both files");
                        MuxAssert.AreEqual(Path.GetFullPath(user), loaded.Sources[0], "user file first");
                    }),

                    Case("CapDropsOutermostFirst", "When over the cap, the outermost files are dropped", (string root) =>
                    {
                        string inner = Path.Combine(root, "inner");
                        Directory.CreateDirectory(inner);
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), new string('o', 100));
                        File.WriteAllText(Path.Combine(inner, "AGENTS.md"), new string('i', 50));

                        ProjectInstructions loaded = new ProjectInstructionsLoader { MaxBytes = 60 }.Load(inner);
                        MuxAssert.AreEqual(1, loaded.Sources.Count, "only the nearest file kept");
                        MuxAssert.AreEqual(1, loaded.DroppedSources.Count, "outer file reported as dropped");
                        MuxAssert.IsTrue(loaded.Truncated, "truncation flagged");
                        MuxAssert.AreEqual(50L, loaded.TotalBytes, "kept bytes counted");
                    }),

                    Case("CapCutsOversizedInnermost", "A single file larger than the cap is cut short", (string root) =>
                    {
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), new string('x', 500));
                        ProjectInstructions loaded = new ProjectInstructionsLoader { MaxBytes = 40 }.Load(root);
                        MuxAssert.IsTrue(loaded.Truncated, "truncated");
                        MuxAssert.AreEqual(40L, loaded.TotalBytes, "cut to the cap");
                        MuxAssert.Contains("[truncated", loaded.Text, "note appended");
                    }),

                    Case("ZeroCapAndDisabledSettingLoadNothing", "A zero cap or the disabled setting loads nothing", (string root) =>
                    {
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "rules");
                        MuxAssert.IsFalse(new ProjectInstructionsLoader { MaxBytes = 0 }.Load(root).HasContent, "zero cap disables");
                        MuxAssert.IsNull(ProjectInstructionsLoader.FromSettings(new MuxSettings { ProjectInstructionsEnabled = false }), "disabled setting yields no loader");
                        MuxAssert.IsFalse(ProjectInstructionsLoader.LoadForSettings(new MuxSettings { ProjectInstructionsMaxBytes = 0 }, root).HasContent, "zero setting disables");
                        MuxAssert.Contains("rules", ProjectInstructionsLoader.LoadForSettings(new MuxSettings(), root).Text, "defaults load");
                    }),

                    Case("SettingsClampMaxBytes", "projectInstructionsMaxBytes clamps to 0..1048576", (string root) =>
                    {
                        MuxAssert.AreEqual(0, new MuxSettings { ProjectInstructionsMaxBytes = -5 }.ProjectInstructionsMaxBytes, "min");
                        MuxAssert.AreEqual(1048576, new MuxSettings { ProjectInstructionsMaxBytes = int.MaxValue }.ProjectInstructionsMaxBytes, "max");
                        MuxAssert.AreEqual(32768, new MuxSettings().ProjectInstructionsMaxBytes, "default");
                    }),

                    Case("AsyncMatchesSync", "LoadAsync returns the same result as Load", (string root) =>
                    {
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "same either way");
                        ProjectInstructionsLoader loader = new ProjectInstructionsLoader();
                        ProjectInstructions sync = loader.Load(root);
                        ProjectInstructions async = loader.LoadAsync(root, CancellationToken.None).GetAwaiter().GetResult();
                        MuxAssert.AreEqual(sync.Text, async.Text, "same text");
                    }),

                    Case("ResolverAddsSectionBeforeAppend", "The system prompt carries the section before --append-system-prompt text", (string root) =>
                    {
                        MakeRepo(root);
                        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "INSTRUCTION_MARKER");
                        ProjectInstructions loaded = new ProjectInstructionsLoader().Load(root);

                        ResolvedSystemPrompt resolved = SystemPromptResolver.Resolve(
                            "persona for {WorkingDirectory}", new PromptProfile(), true, null, root, false, "APPEND_MARKER", loaded);
                        MuxAssert.Contains("INSTRUCTION_MARKER", resolved.SystemPrompt, "instructions present");
                        MuxAssert.Contains(PromptResolver.Shared.GetEffective("section.projectInstructions"), resolved.SystemPrompt, "lead-in present");
                        MuxAssert.IsTrue(resolved.SystemPrompt.IndexOf("INSTRUCTION_MARKER", StringComparison.Ordinal) < resolved.SystemPrompt.IndexOf("APPEND_MARKER", StringComparison.Ordinal), "append comes last");

                        ResolvedSystemPrompt without = SystemPromptResolver.Resolve("persona", new PromptProfile(), true, null, root, false, null);
                        MuxAssert.DoesNotContain("INSTRUCTION_MARKER", without.SystemPrompt, "nothing added without instructions");
                        MuxAssert.AreEqual(string.Empty, SystemPromptResolver.BuildProjectInstructionsSection(new ProjectInstructions()), "empty section for empty instructions");
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, Action<string> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) =>
            {
                string root = Path.Combine(Path.GetTempPath(), "mux-instr-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                try
                {
                    body(root);
                }
                finally
                {
                    try { Directory.Delete(root, true); } catch (Exception) { }
                }

                return Task.CompletedTask;
            });
        }

        private static void MakeRepo(string directory)
        {
            Directory.CreateDirectory(Path.Combine(directory, ".git"));
        }

        #endregion
    }
}
