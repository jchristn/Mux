namespace Mux.Core.Worktrees
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;

    /// <summary>
    /// Creates, finishes, lists, removes, and prunes the git worktrees that isolate subagents and jobs. Each isolated
    /// run gets <c>git worktree add</c> on a new <c>mux/&lt;kind&gt;/&lt;label&gt;</c> branch, starting from the main
    /// tree's HEAD, in a directory under the repository's git directory (<c>.git/mux-worktrees/</c>), so it never shows
    /// up in the user's working tree. When the run ends, a worktree with no changes is removed along with its branch; a
    /// worktree with changes is kept, with any uncommitted changes committed onto its branch so the work can be merged.
    /// <para>
    /// The manager only ever touches worktrees and branches it created: it never checks out, resets, stashes, or
    /// commits in the user's own working tree or branch. Stateless apart from the files it writes; safe to share.
    /// </para>
    /// </summary>
    public sealed class WorktreeManager
    {
        #region Private-Members

        private const string FolderName = "mux-worktrees";
        private const string MetadataSuffix = ".mux.json";
        private const string BranchPrefix = "mux/";

        private static readonly Regex _Unsafe = new Regex("[^a-z0-9-]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { WriteIndented = true };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parses an isolation setting: <c>worktree</c> (or <c>git-worktree</c>), or <c>none</c> / <c>shared</c> / empty.
        /// </summary>
        /// <param name="value">The text.</param>
        /// <param name="mode">The parsed mode.</param>
        /// <returns>True when the text is recognized.</returns>
        public static bool TryParseIsolation(string? value, out IsolationModeEnum mode)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");
            switch (normalized)
            {
                case "":
                case "none":
                case "shared":
                    mode = IsolationModeEnum.None;
                    return true;
                case "worktree":
                case "git-worktree":
                    mode = IsolationModeEnum.Worktree;
                    return true;
                default:
                    mode = IsolationModeEnum.None;
                    return false;
            }
        }

        /// <summary>
        /// Creates an isolated worktree for a run.
        /// </summary>
        /// <param name="workingDirectory">Any directory inside the repository; the run gets the matching subdirectory of the worktree.</param>
        /// <param name="kind">The kind of run (<c>subagent</c> or <c>job</c>), used in the branch and directory names.</param>
        /// <param name="label">The run label (a subagent name or job id).</param>
        /// <param name="cancellationToken">Cancels the git commands.</param>
        /// <returns>The lease describing the worktree.</returns>
        /// <exception cref="InvalidOperationException">Thrown outside a git repository, in a repository without commits, or when git fails.</exception>
        public async Task<WorktreeLease> CreateAsync(string workingDirectory, string kind, string label, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException("Worktree isolation needs an existing working directory; '" + workingDirectory + "' does not exist.");
            }

            string top = await RequireRepositoryAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            GitOutput head = await GitAsync(workingDirectory, cancellationToken, "rev-parse", "--verify", "HEAD^{commit}").ConfigureAwait(false);
            if (head.ExitCode != 0)
            {
                throw new InvalidOperationException("Worktree isolation needs at least one commit; the repository at " + top + " has none yet.");
            }

            string baseCommit = head.StdOut.Trim();
            string prefix = (await GitAsync(workingDirectory, cancellationToken, "rev-parse", "--show-prefix").ConfigureAwait(false)).StdOut.Trim();
            string root = await GetRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(root);

            string safeKind = Sanitize(kind, "run");
            string safeLabel = Sanitize(label, "run");
            string name = string.Empty;
            string branch = string.Empty;
            string path = string.Empty;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string suffix = attempt == 1 ? string.Empty : "-" + attempt.ToString(CultureInfo.InvariantCulture);
                name = safeKind + "-" + safeLabel + suffix;
                branch = BranchPrefix + safeKind + "/" + safeLabel + suffix;
                path = Path.Combine(root, name);
                bool branchTaken = (await GitAsync(top, cancellationToken, "show-ref", "--verify", "--quiet", "refs/heads/" + branch).ConfigureAwait(false)).ExitCode == 0;
                if (!branchTaken && !Directory.Exists(path) && !File.Exists(path + MetadataSuffix))
                {
                    break;
                }
            }

            bool dirty = (await GitAsync(top, cancellationToken, "status", "--porcelain").ConfigureAwait(false)).StdOut.Trim().Length > 0;
            GitOutput added = await GitAsync(top, cancellationToken, "worktree", "add", "-b", branch, path, baseCommit).ConfigureAwait(false);
            if (added.ExitCode != 0)
            {
                throw new InvalidOperationException("git worktree add failed: " + FirstLine(added.StdErr, added.StdOut));
            }

            string isolated = string.IsNullOrEmpty(prefix) ? path : Path.Combine(path, prefix.TrimEnd('/', '\\').Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(isolated);
            WorktreeLease lease = new WorktreeLease
            {
                Name = name,
                Kind = safeKind,
                Label = label ?? string.Empty,
                RepositoryRoot = top,
                Path = path,
                WorkingDirectory = isolated,
                Branch = branch,
                BaseCommit = baseCommit,
                BaseDirty = dirty,
                CreatedUtc = DateTime.UtcNow
            };
            File.WriteAllText(path + MetadataSuffix, JsonSerializer.Serialize(lease, _Json));
            return lease;
        }

        /// <summary>
        /// Ends an isolated run: commits any uncommitted changes onto the worktree's branch, then removes the worktree
        /// and branch when the run changed nothing, or keeps them and reports the commits and diff summary.
        /// </summary>
        /// <param name="lease">The lease from <see cref="CreateAsync"/>. Must not be null.</param>
        /// <param name="cancellationToken">Cancels the git commands.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lease"/> is null.</exception>
        public async Task<WorktreeOutcome> FinishAsync(WorktreeLease lease, CancellationToken cancellationToken)
        {
            if (lease == null) throw new ArgumentNullException(nameof(lease));

            WorktreeOutcome outcome = new WorktreeOutcome { Name = lease.Name, Path = lease.Path, Branch = lease.Branch, BaseCommit = lease.BaseCommit };
            if (!Directory.Exists(lease.Path))
            {
                outcome.Warning = "The worktree directory no longer exists.";
                return outcome;
            }

            string status = (await GitAsync(lease.Path, cancellationToken, "status", "--porcelain").ConfigureAwait(false)).StdOut;
            if (status.Trim().Length > 0)
            {
                await GitAsync(lease.Path, cancellationToken, "add", "-A").ConfigureAwait(false);
                GitOutput committed = await CommitAsync(lease.Path, "mux: changes from " + lease.Kind + " " + lease.Label, cancellationToken).ConfigureAwait(false);
                if (committed.ExitCode == 0)
                {
                    outcome.CommittedLeftovers = true;
                }
                else
                {
                    outcome.Warning = "Could not commit the run's changes: " + FirstLine(committed.StdErr, committed.StdOut);
                }
            }

            string range = lease.BaseCommit + "..HEAD";
            GitOutput log = await GitAsync(lease.Path, cancellationToken, "log", "--format=%h %s", "-n", "50", range).ConfigureAwait(false);
            foreach (string line in SplitLines(log.StdOut))
            {
                outcome.Commits.Add(line);
            }

            bool uncommittedLeft = (await GitAsync(lease.Path, cancellationToken, "status", "--porcelain").ConfigureAwait(false)).StdOut.Trim().Length > 0;
            outcome.Changed = outcome.Commits.Count > 0 || uncommittedLeft;
            if (!outcome.Changed)
            {
                string? removeError = await RemoveCoreAsync(lease.RepositoryRoot, lease.Path, lease.Branch, cancellationToken).ConfigureAwait(false);
                outcome.Removed = removeError == null;
                outcome.Warning ??= removeError;
                return outcome;
            }

            outcome.DiffStat = (await GitAsync(lease.Path, cancellationToken, "diff", "--stat", lease.BaseCommit).ConfigureAwait(false)).StdOut.TrimEnd();
            return outcome;
        }

        /// <summary>
        /// Lists the mux-owned worktrees of the repository that contains <paramref name="workingDirectory"/>.
        /// </summary>
        /// <param name="workingDirectory">Any directory inside the repository.</param>
        /// <param name="cancellationToken">Cancels the git commands.</param>
        /// <returns>The worktrees, in git's order.</returns>
        /// <exception cref="InvalidOperationException">Thrown outside a git repository.</exception>
        public async Task<IReadOnlyList<WorktreeInfo>> ListAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            await RequireRepositoryAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            string root = await GetRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            GitOutput listed = await GitAsync(workingDirectory, cancellationToken, "worktree", "list", "--porcelain").ConfigureAwait(false);
            List<WorktreeInfo> result = new List<WorktreeInfo>();
            WorktreeInfo? current = null;
            foreach (string raw in (listed.StdOut + "\n").Replace("\r", string.Empty).Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("worktree ", StringComparison.Ordinal))
                {
                    current = new WorktreeInfo { Path = line.Substring(9) };
                    current.Name = Path.GetFileName(current.Path.TrimEnd('/', '\\'));
                }
                else if (current != null && line.StartsWith("HEAD ", StringComparison.Ordinal))
                {
                    current.Head = line.Substring(5);
                }
                else if (current != null && line.StartsWith("branch refs/heads/", StringComparison.Ordinal))
                {
                    current.Branch = line.Substring(18);
                }
                else if (line.Length == 0 && current != null)
                {
                    if (IsMuxWorktree(current, root))
                    {
                        result.Add(current);
                    }

                    current = null;
                }
            }

            foreach (WorktreeInfo info in result)
            {
                await FillDetailsAsync(info, root, workingDirectory, cancellationToken).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// Removes one mux worktree and, unless asked to keep it, its branch.
        /// </summary>
        /// <param name="workingDirectory">Any directory inside the repository.</param>
        /// <param name="target">The worktree name, branch, or path.</param>
        /// <param name="force">Remove even when the worktree has uncommitted changes or its branch has commits.</param>
        /// <param name="keepBranch">Keep the branch (and its commits) and remove only the worktree directory.</param>
        /// <param name="cancellationToken">Cancels the git commands.</param>
        /// <returns>The removed worktree.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no mux worktree matches, or when work would be lost without <paramref name="force"/>.</exception>
        public async Task<WorktreeInfo> RemoveAsync(string workingDirectory, string target, bool force, bool keepBranch, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("Name the worktree to remove (its name, branch, or path).");
            string top = await RequireRepositoryAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<WorktreeInfo> all = await ListAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            string wanted = target.Trim();

            // A path may reach git through a symlink (for example /var and /private/var on macOS), so a path target is
            // matched by its mux-worktrees/<name> tail rather than character for character.
            string trimmedTarget = wanted.TrimEnd('/', '\\');
            string? pathName = trimmedTarget.IndexOfAny(new[] { '/', '\\' }) >= 0
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(trimmedTarget) ?? string.Empty), FolderName, StringComparison.Ordinal)
                ? Path.GetFileName(trimmedTarget)
                : null;
            WorktreeInfo? found = null;
            foreach (WorktreeInfo info in all)
            {
                if (string.Equals(info.Name, wanted, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(info.Branch, wanted, StringComparison.Ordinal)
                    || string.Equals(info.Path.TrimEnd('/', '\\'), trimmedTarget, StringComparison.Ordinal)
                    || (pathName != null && string.Equals(info.Name, pathName, StringComparison.Ordinal)))
                {
                    found = info;
                    break;
                }
            }

            if (found == null)
            {
                throw new InvalidOperationException("No mux worktree matches '" + target + "'. mux worktree list shows them.");
            }

            if (!force && found.Dirty)
            {
                throw new InvalidOperationException("Worktree " + found.Name + " has uncommitted changes; pass --force to discard them.");
            }

            if (!force && !keepBranch && found.CommitsAhead != 0)
            {
                string commits = found.CommitsAhead < 0 ? "commits that may not be merged" : found.CommitsAhead + " commit(s) not on its base";
                throw new InvalidOperationException("Branch " + found.Branch + " has " + commits + "; merge them, pass --keep-branch to keep the branch, or --force to delete it.");
            }

            string? error = await RemoveCoreAsync(top, found.Path, keepBranch ? string.Empty : found.Branch, cancellationToken).ConfigureAwait(false);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            return found;
        }

        /// <summary>
        /// Cleans up leftovers: forgets worktrees whose directories are gone, and removes mux worktrees that have no
        /// changes and no commits. Worktrees with uncommitted changes or commits are never touched.
        /// </summary>
        /// <param name="workingDirectory">Any directory inside the repository.</param>
        /// <param name="cancellationToken">Cancels the git commands.</param>
        /// <returns>The names of the worktrees removed or forgotten.</returns>
        /// <exception cref="InvalidOperationException">Thrown outside a git repository.</exception>
        public async Task<IReadOnlyList<string>> PruneAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            string top = await RequireRepositoryAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            string root = await GetRootAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            List<string> pruned = new List<string>();
            IReadOnlyList<WorktreeInfo> before = await ListAsync(workingDirectory, cancellationToken).ConfigureAwait(false);
            foreach (WorktreeInfo info in before)
            {
                if (!info.Exists)
                {
                    pruned.Add(info.Name);
                    if (info.CommitsAhead == 0 && info.Branch.Length > 0)
                    {
                        await GitAsync(top, cancellationToken, "branch", "-D", info.Branch).ConfigureAwait(false);
                    }

                    TryDelete(Path.Combine(root, info.Name) + MetadataSuffix);
                }
                else if (!info.Dirty && info.CommitsAhead == 0)
                {
                    string? error = await RemoveCoreAsync(top, info.Path, info.Branch, cancellationToken).ConfigureAwait(false);
                    if (error == null)
                    {
                        pruned.Add(info.Name);
                    }
                }
            }

            await GitAsync(top, cancellationToken, "worktree", "prune").ConfigureAwait(false);
            return pruned;
        }

        #endregion

        #region Private-Methods

        private static bool IsMuxWorktree(WorktreeInfo info, string root)
        {
            string normalizedPath = info.Path.Replace('\\', '/');
            return info.Branch.StartsWith(BranchPrefix, StringComparison.Ordinal)
                || normalizedPath.Contains("/" + FolderName + "/", StringComparison.Ordinal)
                || normalizedPath.StartsWith(root.Replace('\\', '/'), StringComparison.Ordinal);
        }

        private async Task FillDetailsAsync(WorktreeInfo info, string root, string repositoryDirectory, CancellationToken cancellationToken)
        {
            info.Exists = Directory.Exists(info.Path);
            string metadataPath = Path.Combine(root, info.Name) + MetadataSuffix;
            if (File.Exists(metadataPath))
            {
                try
                {
                    WorktreeLease? lease = JsonSerializer.Deserialize<WorktreeLease>(File.ReadAllText(metadataPath));
                    if (lease != null)
                    {
                        info.BaseCommit = lease.BaseCommit;
                        info.Kind = lease.Kind;
                        info.CreatedUtc = lease.CreatedUtc;
                    }
                }
                catch (Exception)
                {
                    // A damaged metadata file only loses the base commit; the worktree itself is still listed.
                }
            }

            if (info.Exists)
            {
                info.Dirty = (await GitAsync(info.Path, cancellationToken, "status", "--porcelain").ConfigureAwait(false)).StdOut.Trim().Length > 0;
            }

            if (info.BaseCommit.Length > 0 && info.Branch.Length > 0)
            {
                GitOutput count = await GitAsync(repositoryDirectory, cancellationToken, "rev-list", "--count", info.BaseCommit + ".." + "refs/heads/" + info.Branch).ConfigureAwait(false);
                if (count.ExitCode == 0 && int.TryParse(count.StdOut.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ahead))
                {
                    info.CommitsAhead = ahead;
                }
            }
        }

        private async Task<string?> RemoveCoreAsync(string repositoryRoot, string path, string branch, CancellationToken cancellationToken)
        {
            string? error = null;
            if (Directory.Exists(path))
            {
                GitOutput removed = await GitAsync(repositoryRoot, cancellationToken, "worktree", "remove", "--force", path).ConfigureAwait(false);
                if (removed.ExitCode != 0)
                {
                    error = "git worktree remove failed: " + FirstLine(removed.StdErr, removed.StdOut);
                }
            }

            await GitAsync(repositoryRoot, cancellationToken, "worktree", "prune").ConfigureAwait(false);
            if (error == null && !string.IsNullOrEmpty(branch) && branch.StartsWith(BranchPrefix, StringComparison.Ordinal))
            {
                GitOutput deleted = await GitAsync(repositoryRoot, cancellationToken, "branch", "-D", branch).ConfigureAwait(false);
                if (deleted.ExitCode != 0)
                {
                    error = "Could not delete branch " + branch + ": " + FirstLine(deleted.StdErr, deleted.StdOut);
                }
            }

            if (error == null)
            {
                TryDelete(path.TrimEnd('/', '\\') + MetadataSuffix);
            }

            return error;
        }

        private async Task<GitOutput> CommitAsync(string path, string message, CancellationToken cancellationToken)
        {
            // Commit with the user's identity when git has one; otherwise with a mux identity, so an unconfigured
            // machine does not fail. Signing and commit hooks are skipped: this is an automated commit on a mux-owned
            // branch, and a signing prompt or interactive hook would hang the run.
            bool hasIdentity = (await GitAsync(path, cancellationToken, "config", "user.email").ConfigureAwait(false)).StdOut.Trim().Length > 0;
            List<string> args = new List<string>();
            if (!hasIdentity)
            {
                args.AddRange(new[] { "-c", "user.name=mux", "-c", "user.email=mux@localhost" });
            }

            args.AddRange(new[] { "-c", "commit.gpgsign=false", "commit", "--no-verify", "-q", "-m", message });
            return await GitAsync(path, cancellationToken, args.ToArray()).ConfigureAwait(false);
        }

        private async Task<string> RequireRepositoryAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            {
                throw new InvalidOperationException("'" + workingDirectory + "' does not exist.");
            }

            GitOutput top = await GitAsync(workingDirectory, cancellationToken, "rev-parse", "--show-toplevel").ConfigureAwait(false);
            if (top.ExitCode != 0 || top.StdOut.Trim().Length == 0)
            {
                throw new InvalidOperationException("Worktree isolation needs a git repository; " + workingDirectory + " is not inside one.");
            }

            return top.StdOut.Trim();
        }

        private async Task<string> GetRootAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            string common = (await GitAsync(workingDirectory, cancellationToken, "rev-parse", "--git-common-dir").ConfigureAwait(false)).StdOut.Trim();
            string full = Path.IsPathRooted(common) ? common : Path.GetFullPath(Path.Combine(workingDirectory, common));
            return Path.Combine(full, FolderName);
        }

        private static string Sanitize(string? value, string fallback)
        {
            string cleaned = _Unsafe.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), "-").Trim('-');
            if (cleaned.Length > 40)
            {
                cleaned = cleaned.Substring(0, 40).Trim('-');
            }

            return cleaned.Length == 0 ? fallback : cleaned;
        }

        private static string FirstLine(string primary, string secondary)
        {
            string text = (primary ?? string.Empty).Trim().Length > 0 ? primary! : (secondary ?? string.Empty);
            foreach (string line in SplitLines(text))
            {
                return line;
            }

            return "unknown error";
        }

        private static IEnumerable<string> SplitLines(string text)
        {
            foreach (string line in (text ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                if (line.Trim().Length > 0)
                {
                    yield return line.Trim();
                }
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        private static async Task<GitOutput> GitAsync(string workingDirectory, CancellationToken cancellationToken, params string[] arguments)
        {
            ProcessStartInfo info = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (string argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["GIT_TERMINAL_PROMPT"] = "0";
            try
            {
                using (Process process = Process.Start(info)!)
                {
                    Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                    Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    return new GitOutput(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                return new GitOutput(127, string.Empty, "git is not installed or not on PATH: " + ex.Message);
            }
        }

        #endregion
    }
}
