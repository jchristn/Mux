namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Subagents;
    using Mux.Core.Tasks;
    using Mux.Core.Tools.Tools;
    using Mux.Core.Worktrees;
    using Test.Shared.Support;
    using Touchstone.Core;
    using TUIKit.Terminal;

    /// <summary>
    /// Touchstone suite for git worktree isolation (row 28): <see cref="WorktreeManager"/> against real temporary
    /// repositories (create, subdirectory mapping, keep versus auto-remove, leftover commits, branch name collisions,
    /// dirty base trees, refusals outside git and without commits, list, remove, prune), isolated subagents through
    /// <c>spawn_subagent</c>, isolated jobs through <see cref="JobManager"/> and <see cref="TaskOrchestrator"/>, the
    /// <c>mux worktree</c> verb, and <c>/worktrees</c>. Every case also checks that the main working tree and branch are
    /// untouched. Skipped when git is not on PATH.
    /// </summary>
    public static class WorktreeIsolationSuite
    {
        #region Private-Members

        private const string SuiteId = "WorktreeIsolation";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the worktree isolation suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the worktree cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool git = GitFixture.IsAvailable();
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<string, CancellationToken, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => WithTempAsync((string root) => body(root, ct)), skip: !git, skipReason: "git is not on PATH"));
            }

            // --- manager ---
            Add("CreateMakesBranchAndWorktree", "Create adds a worktree on a new mux branch under the git directory, from HEAD", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeLease lease = await new WorktreeManager().CreateAsync(repo.Directory, "subagent", "Code Reviewer", ct).ConfigureAwait(false);
                MuxAssert.AreEqual("mux/subagent/code-reviewer", lease.Branch, "sanitized branch");
                MuxAssert.AreEqual("subagent-code-reviewer", lease.Name, "name");
                MuxAssert.Contains(Path.Combine(".git", "mux-worktrees"), lease.Path, "under the git directory");
                MuxAssert.AreEqual(lease.Path, lease.WorkingDirectory, "top-level start maps to the worktree root");
                MuxAssert.AreEqual(head, lease.BaseCommit, "starts from HEAD");
                MuxAssert.IsFalse(lease.BaseDirty, "clean base");
                MuxAssert.AreEqual("hello\n", File.ReadAllText(Path.Combine(lease.WorkingDirectory, "app.txt")).Replace("\r", string.Empty), "committed content is there");
                MuxAssert.IsTrue(File.Exists(lease.Path + ".mux.json"), "metadata written");
                MuxAssert.Contains("mux/subagent/code-reviewer", repo.Run("worktree", "list", "--porcelain"), "git knows the worktree");
                AssertMainUntouched(repo, head);
            });
            Add("SubdirectoryMapsIntoWorktree", "Starting in a subdirectory gives the run the same subdirectory of the worktree", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                repo.Write("src/lib/code.txt", "x").Commit("add src");
                WorktreeLease lease = await new WorktreeManager().CreateAsync(Path.Combine(repo.Directory, "src", "lib"), "job", "j1", ct).ConfigureAwait(false);
                MuxAssert.AreEqual(Path.Combine(lease.Path, "src", "lib"), lease.WorkingDirectory, "subdirectory mapped");
                MuxAssert.IsTrue(File.Exists(Path.Combine(lease.WorkingDirectory, "code.txt")), "files present");
            });
            Add("UnchangedRunIsRemoved", "Finishing a run that changed nothing removes the worktree, its branch, and its metadata", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease lease = await manager.CreateAsync(repo.Directory, "subagent", "reader", ct).ConfigureAwait(false);
                WorktreeOutcome outcome = await manager.FinishAsync(lease, ct).ConfigureAwait(false);
                MuxAssert.IsFalse(outcome.Changed, "no changes");
                MuxAssert.IsTrue(outcome.Removed, "removed");
                MuxAssert.IsFalse(Directory.Exists(lease.Path), "directory gone");
                MuxAssert.IsFalse(File.Exists(lease.Path + ".mux.json"), "metadata gone");
                MuxAssert.IsFalse(BranchExists(repo, lease.Branch), "branch gone");
                AssertMainUntouched(repo, head);
            });
            Add("ChangedRunIsKeptAndCommitted", "Finishing a run with uncommitted edits commits them onto its branch and keeps the worktree", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease lease = await manager.CreateAsync(repo.Directory, "subagent", "writer", ct).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(lease.WorkingDirectory, "feature.txt"), "new work\n");
                File.WriteAllText(Path.Combine(lease.WorkingDirectory, "app.txt"), "changed\n");
                WorktreeOutcome outcome = await manager.FinishAsync(lease, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(outcome.Changed && !outcome.Removed, "kept");
                MuxAssert.IsTrue(outcome.CommittedLeftovers, "leftovers committed");
                MuxAssert.AreEqual(1, outcome.Commits.Count, "one commit");
                MuxAssert.Contains("mux: changes from subagent writer", outcome.Commits[0], "commit subject");
                MuxAssert.Contains("feature.txt", outcome.DiffStat, "diff stat names the new file");
                MuxAssert.Contains("app.txt", outcome.DiffStat, "diff stat names the edit");
                MuxAssert.IsTrue(Directory.Exists(lease.Path), "worktree kept");
                MuxAssert.Contains("new work", repo.Run("show", lease.Branch + ":feature.txt"), "work is on the branch");
                MuxAssert.IsFalse(File.Exists(Path.Combine(repo.Directory, "feature.txt")), "main tree does not get the file");
                AssertMainUntouched(repo, head);
            });
            Add("RunCommitsAreReported", "Commits the run made itself are reported, and nothing extra is committed", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease lease = await manager.CreateAsync(repo.Directory, "job", "j9", ct).ConfigureAwait(false);
                CommitIn(lease.Path, "one.txt", "1", "first change");
                CommitIn(lease.Path, "two.txt", "2", "second change");
                WorktreeOutcome outcome = await manager.FinishAsync(lease, ct).ConfigureAwait(false);
                MuxAssert.IsFalse(outcome.CommittedLeftovers, "nothing left to commit");
                MuxAssert.AreEqual(2, outcome.Commits.Count, "two commits");
                MuxAssert.Contains("second change", outcome.Commits[0], "newest first");
                AssertMainUntouched(repo, head);
            });
            Add("BranchNameCollisionsGetSuffixes", "An existing branch or worktree with the same name gets a numbered suffix", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                repo.Run("branch", "mux/subagent/reviewer");
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease first = await manager.CreateAsync(repo.Directory, "subagent", "reviewer", ct).ConfigureAwait(false);
                WorktreeLease second = await manager.CreateAsync(repo.Directory, "subagent", "reviewer", ct).ConfigureAwait(false);
                MuxAssert.AreEqual("mux/subagent/reviewer-2", first.Branch, "the existing branch is skipped");
                MuxAssert.AreEqual("mux/subagent/reviewer-3", second.Branch, "a live worktree is skipped");
                MuxAssert.AreNotEqual(first.Path, second.Path, "separate directories");
                MuxAssert.AreEqual(head, repo.Run("rev-parse", "mux/subagent/reviewer").Trim(), "the user's branch is untouched");
                AssertMainUntouched(repo, head);
            });
            Add("DirtyBaseIsFlaggedNotCopied", "Uncommitted changes in the main tree are flagged, not copied, and survive the run", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                File.WriteAllText(Path.Combine(repo.Directory, "app.txt"), "my uncommitted edit\n");
                File.WriteAllText(Path.Combine(repo.Directory, "scratch.txt"), "untracked\n");
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease lease = await manager.CreateAsync(repo.Directory, "subagent", "x", ct).ConfigureAwait(false);
                MuxAssert.IsTrue(lease.BaseDirty, "dirty base flagged");
                MuxAssert.AreEqual("hello\n", File.ReadAllText(Path.Combine(lease.WorkingDirectory, "app.txt")).Replace("\r", string.Empty), "worktree starts from HEAD");
                MuxAssert.IsFalse(File.Exists(Path.Combine(lease.WorkingDirectory, "scratch.txt")), "untracked files not copied");
                await manager.FinishAsync(lease, ct).ConfigureAwait(false);
                MuxAssert.AreEqual("my uncommitted edit\n", File.ReadAllText(Path.Combine(repo.Directory, "app.txt")).Replace("\r", string.Empty), "user's edit survives");
                MuxAssert.IsTrue(File.Exists(Path.Combine(repo.Directory, "scratch.txt")), "user's untracked file survives");
                MuxAssert.AreEqual(head, repo.Run("rev-parse", "HEAD").Trim(), "HEAD unchanged");
                MuxAssert.AreEqual(string.Empty, RunGitNoThrow(repo, "stash", "list").Trim(), "no stash entries");
            });
            Add("RefusesOutsideGitAndWithoutCommits", "Create refuses outside a repository, in a repository without commits, and for a missing directory", async (string root, CancellationToken ct) =>
            {
                WorktreeManager manager = new WorktreeManager();
                string plain = Path.Combine(root, "plain");
                Directory.CreateDirectory(plain);
                InvalidOperationException outside = await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync(plain, "subagent", "x", ct), "outside git").ConfigureAwait(false);
                MuxAssert.Contains("not inside one", outside.Message, "explains");
                GitFixture empty = new GitFixture(Path.Combine(root, "empty"));
                InvalidOperationException noCommits = await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync(empty.Directory, "subagent", "x", ct), "no commits").ConfigureAwait(false);
                MuxAssert.Contains("has none yet", noCommits.Message, "explains");
                await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.CreateAsync(Path.Combine(root, "missing"), "subagent", "x", ct), "missing directory").ConfigureAwait(false);
                await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.ListAsync(plain, ct), "list outside git").ConfigureAwait(false);
                await MuxAssert.ThrowsAsync<ArgumentNullException>(() => manager.FinishAsync(null!, ct), "null lease").ConfigureAwait(false);
            });
            Add("ListShowsOnlyMuxWorktrees", "List shows mux worktrees with their state and leaves the user's own worktrees out", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                repo.Run("worktree", "add", "-q", "-b", "feature", Path.Combine(root, "mine"));
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease clean = await manager.CreateAsync(repo.Directory, "job", "j1", ct).ConfigureAwait(false);
                WorktreeLease dirty = await manager.CreateAsync(repo.Directory, "job", "j2", ct).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(dirty.Path, "wip.txt"), "wip");
                IReadOnlyList<WorktreeInfo> list = await manager.ListAsync(repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(2, list.Count, "two mux worktrees: " + string.Join(", ", new List<WorktreeInfo>(list).ConvertAll(i => i.Name)));
                WorktreeInfo first = list[0].Name == "job-j1" ? list[0] : list[1];
                WorktreeInfo second = list[0].Name == "job-j2" ? list[0] : list[1];
                MuxAssert.IsTrue(first.Exists && !first.Dirty && first.CommitsAhead == 0, "clean one");
                MuxAssert.IsTrue(second.Dirty, "dirty one");
                MuxAssert.AreEqual("job", first.Kind, "kind from metadata");
                MuxAssert.AreEqual(head, first.BaseCommit, "base from metadata");
                MuxAssert.Contains("no changes", WorktreeCommand.Describe(first), "describe clean");
                MuxAssert.Contains("uncommitted changes", WorktreeCommand.Describe(second), "describe dirty");
            });
            Add("RemoveProtectsWork", "Remove refuses to lose uncommitted changes or commits unless forced, and can keep the branch", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease dirty = await manager.CreateAsync(repo.Directory, "job", "dirty", ct).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(dirty.Path, "wip.txt"), "wip");
                InvalidOperationException refuseDirty = await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveAsync(repo.Directory, "job-dirty", false, false, ct), "dirty refused").ConfigureAwait(false);
                MuxAssert.Contains("uncommitted changes", refuseDirty.Message, "explains");

                WorktreeLease committed = await manager.CreateAsync(repo.Directory, "job", "done", ct).ConfigureAwait(false);
                CommitIn(committed.Path, "done.txt", "done", "finished");
                InvalidOperationException refuseCommits = await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveAsync(repo.Directory, committed.Branch, false, false, ct), "commits refused").ConfigureAwait(false);
                MuxAssert.Contains("1 commit(s)", refuseCommits.Message, "counts commits");
                WorktreeInfo kept = await manager.RemoveAsync(repo.Directory, "job-done", false, true, ct).ConfigureAwait(false);
                MuxAssert.IsFalse(Directory.Exists(committed.Path), "directory removed");
                MuxAssert.IsTrue(BranchExists(repo, kept.Branch), "branch kept with --keep-branch");

                await manager.RemoveAsync(repo.Directory, dirty.Path, true, false, ct).ConfigureAwait(false);
                MuxAssert.IsFalse(Directory.Exists(dirty.Path), "forced removal");
                MuxAssert.IsFalse(BranchExists(repo, dirty.Branch), "forced removal deletes the branch");
                await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveAsync(repo.Directory, "nope", true, false, ct), "unknown name").ConfigureAwait(false);
                await MuxAssert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveAsync(repo.Directory, " ", true, false, ct), "blank name").ConfigureAwait(false);
                AssertMainUntouched(repo, head);
            });
            Add("PruneRemovesOnlyUnchangedAndMissing", "Prune removes clean worktrees and forgets missing ones, and never touches work", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease clean = await manager.CreateAsync(repo.Directory, "job", "clean", ct).ConfigureAwait(false);
                WorktreeLease changed = await manager.CreateAsync(repo.Directory, "job", "changed", ct).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(changed.Path, "work.txt"), "work");
                await manager.FinishAsync(changed, ct).ConfigureAwait(false);
                WorktreeLease dirty = await manager.CreateAsync(repo.Directory, "job", "dirty", ct).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(dirty.Path, "wip.txt"), "wip");
                WorktreeLease missing = await manager.CreateAsync(repo.Directory, "job", "missing", ct).ConfigureAwait(false);
                Directory.Delete(missing.Path, true);

                IReadOnlyList<string> pruned = await manager.PruneAsync(repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(pruned.Contains("job-clean") && pruned.Contains("job-missing"), "pruned clean and missing: " + string.Join(", ", pruned));
                MuxAssert.IsFalse(pruned.Contains("job-changed") || pruned.Contains("job-dirty"), "work kept");
                MuxAssert.IsFalse(Directory.Exists(clean.Path), "clean removed");
                MuxAssert.IsFalse(BranchExists(repo, missing.Branch), "missing one's empty branch deleted");
                MuxAssert.IsTrue(Directory.Exists(changed.Path) && BranchExists(repo, changed.Branch), "changed kept");
                MuxAssert.IsTrue(File.Exists(Path.Combine(dirty.Path, "wip.txt")), "dirty kept");
                IReadOnlyList<WorktreeInfo> after = await manager.ListAsync(repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(2, after.Count, "two left");
                MuxAssert.AreEqual(0, (await manager.PruneAsync(repo.Directory, ct).ConfigureAwait(false)).Count, "second prune does nothing");
                AssertMainUntouched(repo, head);
            });
            Add("IsolationParsingAndDefinitions", "Isolation text parses, subagent definitions validate and round-trip it", (string root, CancellationToken ct) =>
            {
                MuxAssert.IsTrue(WorktreeManager.TryParseIsolation("Worktree", out IsolationModeEnum a) && a == IsolationModeEnum.Worktree, "worktree");
                MuxAssert.IsTrue(WorktreeManager.TryParseIsolation("git_worktree", out IsolationModeEnum b) && b == IsolationModeEnum.Worktree, "git_worktree");
                MuxAssert.IsTrue(WorktreeManager.TryParseIsolation(null, out IsolationModeEnum c) && c == IsolationModeEnum.None, "null is none");
                MuxAssert.IsTrue(WorktreeManager.TryParseIsolation("shared", out IsolationModeEnum d) && d == IsolationModeEnum.None, "shared");
                MuxAssert.IsFalse(WorktreeManager.TryParseIsolation("container", out _), "unknown");
                SubagentDefinition definition = JsonSerializer.Deserialize<SubagentDefinition>("{\"name\":\"r\",\"systemPrompt\":\"p\",\"isolation\":\" WorkTree \"}")!;
                MuxAssert.AreEqual("worktree", definition.Isolation, "normalized");
                MuxAssert.AreEqual("worktree", definition.Clone().Isolation, "cloned");
                MuxAssert.IsTrue(SubagentRegistry.IsValid(definition), "valid");
                MuxAssert.IsFalse(SubagentRegistry.IsValid(new SubagentDefinition { Name = "r", SystemPrompt = "p", Isolation = "vm" }), "unknown isolation invalid");
                MuxAssert.IsNull(new SubagentDefinition { Isolation = "  " }.Isolation, "blank is null");
                MuxAssert.DoesNotContain("isolation", JsonSerializer.Serialize(new SubagentDefinition { Name = "r" }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }), "omitted when unset");
                return Task.CompletedTask;
            });

            // --- subagents ---
            Add("IsolatedSubagentKeepsWork", "spawn_subagent with isolation worktree runs in a worktree and reports the kept branch", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                ScriptedSubagentExecutor executor = new ScriptedSubagentExecutor((string dir) => File.WriteAllText(Path.Combine(dir, "review.md"), "# notes\n"));
                SpawnSubagentTool tool = new SpawnSubagentTool(Registry(null), executor);
                Mux.Core.Models.ToolResult result = await tool.ExecuteAsync("c1", Json(new { subagent = "writer", prompt = "write notes", isolation = "worktree" }), repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(result.Success, "succeeded: " + result.Content);
                MuxAssert.Contains("mux-worktrees", executor.WorkingDirectories[0], "ran inside a worktree");
                using (JsonDocument doc = JsonDocument.Parse(result.Content))
                {
                    JsonElement worktree = doc.RootElement.GetProperty("worktree");
                    MuxAssert.IsTrue(worktree.GetProperty("kept").GetBoolean(), "kept");
                    MuxAssert.AreEqual("mux/subagent/writer", worktree.GetProperty("branch").GetString(), "branch");
                    MuxAssert.Contains("review.md", worktree.GetProperty("diff_stat").GetString() ?? string.Empty, "diff stat");
                    MuxAssert.Contains("git merge mux/subagent/writer", worktree.GetProperty("next").GetString() ?? string.Empty, "next step");
                }

                MuxAssert.IsFalse(File.Exists(Path.Combine(repo.Directory, "review.md")), "main tree untouched");
                AssertMainUntouched(repo, head);
            });
            Add("IsolatedSubagentWithoutChangesCleansUp", "An isolated subagent that changes nothing leaves no worktree or branch", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                ScriptedSubagentExecutor executor = new ScriptedSubagentExecutor((string dir) => { });
                SpawnSubagentTool tool = new SpawnSubagentTool(Registry("worktree"), executor);
                Mux.Core.Models.ToolResult result = await tool.ExecuteAsync("c1", Json(new { subagent = "writer", prompt = "look around" }), repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"removed\":true", result.Content, "removed (isolation from the definition)");
                MuxAssert.AreEqual(0, (await new WorktreeManager().ListAsync(repo.Directory, ct).ConfigureAwait(false)).Count, "nothing left");
                MuxAssert.IsFalse(BranchExists(repo, "mux/subagent/writer"), "no branch");

                Mux.Core.Models.ToolResult shared = await tool.ExecuteAsync("c2", Json(new { subagent = "writer", prompt = "look", isolation = "none" }), repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(repo.Directory, executor.WorkingDirectories[1], "the argument overrides the definition");
                MuxAssert.Contains("\"worktree\":null", shared.Content, "no worktree in the shared run");
            });
            Add("IsolatedSubagentErrors", "spawn_subagent reports bad isolation values, unavailable isolation, and executor failures with the worktree", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                SpawnSubagentTool ok = new SpawnSubagentTool(Registry(null), new ScriptedSubagentExecutor((string dir) => { }));
                Mux.Core.Models.ToolResult bad = await ok.ExecuteAsync("c1", Json(new { subagent = "writer", prompt = "x", isolation = "docker" }), repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.Contains("invalid_isolation", bad.Content, "unknown isolation");
                string plain = Path.Combine(root, "plain");
                Directory.CreateDirectory(plain);
                Mux.Core.Models.ToolResult unavailable = await ok.ExecuteAsync("c2", Json(new { subagent = "writer", prompt = "x", isolation = "worktree" }), plain, ct).ConfigureAwait(false);
                MuxAssert.Contains("isolation_unavailable", unavailable.Content, "outside git");
                SpawnSubagentTool failing = new SpawnSubagentTool(Registry(null), new ScriptedSubagentExecutor((string dir) =>
                {
                    File.WriteAllText(Path.Combine(dir, "partial.txt"), "half done");
                    throw new InvalidOperationException("model crashed");
                }));
                Mux.Core.Models.ToolResult failed = await failing.ExecuteAsync("c3", Json(new { subagent = "writer", prompt = "x", isolation = "worktree" }), repo.Directory, ct).ConfigureAwait(false);
                MuxAssert.IsFalse(failed.Success, "failed");
                MuxAssert.Contains("model crashed", failed.Content, "error");
                MuxAssert.Contains("\"kept\":true", failed.Content, "partial work kept for inspection");
                AssertMainUntouched(repo, head);
            });

            // --- jobs ---
            Add("IsolatedJobRunsInItsOwnWorktree", "An isolated job gets a worktree, runs there, and reports its outcome; a shared job does not", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                await using (JobManager manager = new JobManager(WritingRunner, maxConcurrency: 2) { IsolationDirectoryProvider = () => repo.Directory })
                {
                    Job isolated = await manager.EnqueueAsync("write isolated.txt", ApprovalPolicyEnum.AutoApprove, null, IsolationModeEnum.Worktree, ct).ConfigureAwait(false);
                    Job shared = await manager.EnqueueAsync("read only", ApprovalPolicyEnum.AutoApprove, null, ct).ConfigureAwait(false);
                    await WaitTerminalAsync(isolated, ct).ConfigureAwait(false);
                    await WaitTerminalAsync(shared, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(JobState.Completed, isolated.State, "completed: " + isolated.FailureMessage);
                    MuxAssert.IsNotNull(isolated.WorktreeOutcome, "outcome recorded");
                    MuxAssert.AreEqual("mux/job/" + isolated.Id, isolated.WorktreeOutcome!.Branch, "job branch");
                    MuxAssert.IsTrue(isolated.WorktreeOutcome.Changed && !isolated.WorktreeOutcome.Removed, "kept");
                    MuxAssert.Contains("isolated.txt", isolated.WorktreeOutcome.DiffStat, "diff stat");
                    MuxAssert.IsNull(shared.Worktree, "shared job has no worktree");
                    MuxAssert.IsNull(shared.WorktreeOutcome, "shared job has no outcome");
                    MuxAssert.IsFalse(File.Exists(Path.Combine(repo.Directory, "isolated.txt")), "main tree untouched");
                }

                AssertMainUntouched(repo, head);
            });
            Add("IsolatedJobWithoutDirectoryFails", "An isolated job fails clearly when the manager has no base directory or the directory is not a repository", async (string root, CancellationToken ct) =>
            {
                await using (JobManager manager = new JobManager(WritingRunner, maxConcurrency: 1))
                {
                    Job job = await manager.EnqueueAsync("x", ApprovalPolicyEnum.AutoApprove, null, IsolationModeEnum.Worktree, ct).ConfigureAwait(false);
                    await WaitTerminalAsync(job, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(JobState.Failed, job.State, "failed");
                    MuxAssert.Contains("needs a working directory", job.FailureMessage, "explains");
                }

                string plain = Path.Combine(root, "plain");
                Directory.CreateDirectory(plain);
                await using (JobManager manager = new JobManager(WritingRunner, maxConcurrency: 1) { IsolationDirectoryProvider = () => plain })
                {
                    Job job = await manager.EnqueueAsync("x", ApprovalPolicyEnum.AutoApprove, null, IsolationModeEnum.Worktree, ct).ConfigureAwait(false);
                    await WaitTerminalAsync(job, ct).ConfigureAwait(false);
                    MuxAssert.AreEqual(JobState.Failed, job.State, "failed outside git");
                    MuxAssert.Contains("not inside one", job.FailureMessage, "explains");
                }

                AgentLoopOptions template = new AgentLoopOptions(new EndpointConfig { Name = "e", BaseUrl = "http://localhost", Model = "m" }) { WorkingDirectory = root };
                await using (JobManager agentManager = JobManager.CreateForAgentLoop(template))
                {
                    MuxAssert.AreEqual(root, agentManager.IsolationDirectoryProvider!(), "the agent-loop manager uses the template directory");
                }
            });
            Add("OrchestratorIsolatesTasks", "The task orchestrator with IsolateTasks runs each task on its own branch", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                await using (JobManager manager = new JobManager(WritingRunner, maxConcurrency: 2) { IsolationDirectoryProvider = () => repo.Directory })
                {
                    TaskPlan plan = new TaskPlan();
                    plan.SetPlan(new List<AgentTask> { new AgentTask { Id = "t1", Title = "write alpha.txt" }, new AgentTask { Id = "t2", Title = "write beta.txt" } });
                    await new TaskOrchestrator(plan, manager) { IsolateTasks = true }.RunAsync(ct).ConfigureAwait(false);
                    foreach (AgentTask task in plan.Snapshot())
                    {
                        MuxAssert.AreEqual(AgentTaskStatusEnum.Completed, task.Status, task.Id + " completed");
                    }

                    List<string> branches = new List<string>();
                    foreach (Job job in manager.Jobs)
                    {
                        MuxAssert.IsNotNull(job.WorktreeOutcome, job.Id + " isolated");
                        branches.Add(job.WorktreeOutcome!.Branch);
                    }

                    MuxAssert.AreEqual(2, branches.Count, "two jobs");
                    MuxAssert.AreNotEqual(branches[0], branches[1], "separate branches");
                }

                AssertMainUntouched(repo, head);
            });

            // --- CLI and terminal ---
            Add("WorktreeVerb", "mux worktree list, remove, and prune work, and bad usage is rejected", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                WorktreeManager manager = new WorktreeManager();
                WorktreeLease lease = await manager.CreateAsync(repo.Directory, "job", "cli", ct).ConfigureAwait(false);
                CliInvocationResult list = await RunVerbAsync(new[] { "list", "--cwd", repo.Directory }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(0, list.ExitCode, "list ok");
                MuxAssert.Contains("job-cli  mux/job/cli  no changes", list.StdOut, "listed");
                CliInvocationResult json = await RunVerbAsync(new[] { "list", "--cwd", repo.Directory, "--output-format", "json" }, ct).ConfigureAwait(false);
                MuxAssert.Contains("\"branch\": \"mux/job/cli\"", json.StdOut.Replace("\":\"", "\": \""), "json list");
                CliInvocationResult prune = await RunVerbAsync(new[] { "prune", "--cwd", repo.Directory }, ct).ConfigureAwait(false);
                MuxAssert.Contains("Pruned 1: job-cli", prune.StdOut, "pruned");
                CliInvocationResult empty = await RunVerbAsync(new[] { "list", "--cwd", repo.Directory }, ct).ConfigureAwait(false);
                MuxAssert.Contains("No mux worktrees.", empty.StdOut, "empty");
                WorktreeLease again = await manager.CreateAsync(repo.Directory, "job", "rm", ct).ConfigureAwait(false);
                CliInvocationResult removed = await RunVerbAsync(new[] { "remove", "job-rm", "--cwd", repo.Directory }, ct).ConfigureAwait(false);
                MuxAssert.Contains("Removed worktree job-rm and branch mux/job/rm.", removed.StdOut, "removed");
                MuxAssert.AreEqual(2, (await RunVerbAsync(new[] { "remove", "--cwd", repo.Directory }, ct).ConfigureAwait(false)).ExitCode, "remove needs a name");
                MuxAssert.AreEqual(2, (await RunVerbAsync(new[] { "frobnicate" }, ct).ConfigureAwait(false)).ExitCode, "unknown action");
                MuxAssert.AreEqual(2, (await RunVerbAsync(new[] { "list", "--bogus" }, ct).ConfigureAwait(false)).ExitCode, "unknown option");
                CliInvocationResult missing = await RunVerbAsync(new[] { "remove", "nope", "--cwd", repo.Directory }, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(1, missing.ExitCode, "unknown worktree");
                MuxAssert.Contains("No mux worktree matches 'nope'", missing.StdErr, "explains");
                string plain = Path.Combine(root, "plain");
                Directory.CreateDirectory(plain);
                MuxAssert.AreEqual(1, (await RunVerbAsync(new[] { "list", "--cwd", plain }, ct).ConfigureAwait(false)).ExitCode, "outside git");
                AssertMainUntouched(repo, head);
            });
            Add("TerminalWorktreesCommand", "/worktrees lists, prunes, and reports errors in the terminal", async (string root, CancellationToken ct) =>
            {
                GitFixture repo = NewRepo(root, out string head);
                await new WorktreeManager().CreateAsync(repo.Directory, "subagent", "tui", ct).ConfigureAwait(false);
                HeadlessBackend backend = new HeadlessBackend(160, 40);
                await using (JobManager jobs = new JobManager(WritingRunner, maxConcurrency: 1))
                using (MuxTuiApp app = new MuxTuiApp(backend, jobs, "demo", ApprovalPolicyEnum.AutoApprove, workingDirectory: repo.Directory))
                {
                    await SubmitAndWaitAsync(backend, app, "/worktrees", "subagent-tui", ct).ConfigureAwait(false);
                    await SubmitAndWaitAsync(backend, app, "/worktrees remove", "Name a worktree", ct).ConfigureAwait(false);
                    await SubmitAndWaitAsync(backend, app, "/worktrees remove ghost", "No mux worktree matches 'ghost'", ct).ConfigureAwait(false);
                    await SubmitAndWaitAsync(backend, app, "/worktrees frob", "Usage: /worktrees", ct).ConfigureAwait(false);
                    await SubmitAndWaitAsync(backend, app, "/worktrees prune", "Pruned subagent-tui", ct).ConfigureAwait(false);
                    await SubmitAndWaitAsync(backend, app, "/worktrees", "No mux worktrees.", ct).ConfigureAwait(false);
                }

                AssertMainUntouched(repo, head);
            });

            return new TestSuiteDescriptor(SuiteId, "Git worktree isolation for subagents and jobs", cases);
        }

        #endregion

        #region Private-Methods

        private static GitFixture NewRepo(string root, out string head)
        {
            GitFixture repo = new GitFixture(Path.Combine(root, "repo"));
            repo.Write("app.txt", "hello\n");
            head = repo.Commit("initial");
            return repo;
        }

        // Writes a file inside a worktree and commits it there, with a fixed identity and no global config.
        private static void CommitIn(string worktreePath, string file, string content, string message)
        {
            File.WriteAllText(Path.Combine(worktreePath, file), content);
            Git(worktreePath, "add", "-A");
            Git(worktreePath, "-c", "user.name=Mux Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
        }

        private static void Git(string directory, params string[] arguments)
        {
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (string argument in arguments) info.ArgumentList.Add(argument);
            info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)!)
            {
                string error = process.StandardError.ReadToEnd();
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(" ", arguments) + " failed: " + error);
            }
        }

        private static void AssertMainUntouched(GitFixture repo, string head)
        {
            MuxAssert.AreEqual("main", repo.Run("rev-parse", "--abbrev-ref", "HEAD").Trim(), "main tree still on main");
            MuxAssert.AreEqual(head, repo.Run("rev-parse", "HEAD").Trim(), "main HEAD unchanged");
        }

        private static bool BranchExists(GitFixture repo, string branch)
        {
            try
            {
                repo.Run("show-ref", "--verify", "--quiet", "refs/heads/" + branch);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static string RunGitNoThrow(GitFixture repo, params string[] args)
        {
            try { return repo.Run(args); } catch (InvalidOperationException ex) { return ex.Message; }
        }

        private static SubagentRegistry Registry(string? isolation)
        {
            return new SubagentRegistry(new[] { new SubagentDefinition { Name = "writer", SystemPrompt = "You write.", Isolation = isolation } });
        }

        private static JsonElement Json(object value)
        {
            using (JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(value)))
            {
                return document.RootElement.Clone();
            }
        }

        // A job runner that writes the file named in "write <name>" into the job's working directory (its worktree
        // when isolated) and otherwise does nothing.
        private static async IAsyncEnumerable<AgentEvent> WritingRunner(Job job, string prompt, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            if (prompt.StartsWith("write ", StringComparison.Ordinal) && job.Worktree != null)
            {
                File.WriteAllText(Path.Combine(job.Worktree.WorkingDirectory, prompt.Substring(6).Trim()), prompt + "\n");
            }

            yield return new AssistantTextEvent { Text = "ok" };
            yield return new RunCompletedEvent { RunId = Guid.NewGuid().ToString("N"), Status = "completed", IterationsCompleted = 1, DurationMs = 1 };
        }

        private static async Task WaitTerminalAsync(Job job, CancellationToken ct)
        {
            for (int i = 0; i < 600; i++)
            {
                if (job.State == JobState.Completed || job.State == JobState.Failed || job.State == JobState.Cancelled)
                {
                    return;
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            MuxAssert.Fail("job " + job.Id + " did not finish; state " + job.State);
        }

        private static async Task SubmitAndWaitAsync(HeadlessBackend backend, MuxTuiApp app, string command, string expected, CancellationToken ct)
        {
            backend.FeedInput(command + "\r");
            app.PumpInputOnce();
            for (int i = 0; i < 200; i++)
            {
                if (string.Join("\n", app.TranscriptSnapshot()).Contains(expected, StringComparison.Ordinal))
                {
                    return;
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
            }

            MuxAssert.Fail("'" + command + "' did not show '" + expected + "'. Transcript: " + string.Join("\n", app.TranscriptSnapshot()));
        }

        private static async Task<CliInvocationResult> RunVerbAsync(string[] args, CancellationToken ct)
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalErr = Console.Error;
            StringWriter stdout = new StringWriter();
            StringWriter stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                int exit = await new WorktreeCommand().RunAsync(args, ct).ConfigureAwait(false);
                return new CliInvocationResult(exit, stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }

        private static async Task WithTempAsync(Func<string, Task> body)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-wt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                await body(root).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(root, true);
                }
                catch (Exception)
                {
                }
            }
        }

        #endregion
    }
}
