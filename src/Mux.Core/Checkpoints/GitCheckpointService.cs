namespace Mux.Core.Checkpoints
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Captures and restores whole-workspace snapshots using git plumbing, without disturbing the user's
    /// branch, commit history, stash, or staged index. A snapshot is a dangling commit object built from a
    /// throwaway index that stages every tracked and untracked (non-ignored) file, so it records the exact
    /// working-tree content at capture time. Restoring resets the working tree to a snapshot: modified and
    /// deleted files are brought back, and files created after the snapshot are removed.
    /// <para>
    /// This is the substrate for per-turn undo/redo. Its reach is the git working tree only — it cannot
    /// reverse effects outside it (spawned processes, network calls, files under <c>.gitignore</c>). Callers
    /// should treat <see cref="IsRepositoryAsync"/> as the gate: outside a git work tree, checkpointing is
    /// unavailable.
    /// </para>
    /// </summary>
    public sealed class GitCheckpointService
    {
        #region Private-Members

        private readonly string _WorkingDirectory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="GitCheckpointService"/> class.
        /// </summary>
        /// <param name="workingDirectory">The directory to run git in (any path inside the work tree).
        /// Must not be null or empty.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workingDirectory"/> is null or empty.</exception>
        public GitCheckpointService(string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                throw new ArgumentException("Working directory cannot be null or empty.", nameof(workingDirectory));
            }

            _WorkingDirectory = workingDirectory;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Determines whether the working directory is inside a git work tree, which is required for
        /// checkpointing.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>True when git reports the directory is inside a work tree.</returns>
        public async Task<bool> IsRepositoryAsync(CancellationToken cancellationToken)
        {
            GitResult result = await RunGitAsync(new[] { "rev-parse", "--is-inside-work-tree" }, null, cancellationToken).ConfigureAwait(false);
            return result.ExitCode == 0 && result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Captures the current working tree as a dangling snapshot commit and returns its object id.
        /// </summary>
        /// <param name="label">A human-readable label recorded in the snapshot commit message.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The snapshot commit's object id (SHA).</returns>
        /// <exception cref="InvalidOperationException">Thrown when a git command fails.</exception>
        public async Task<string> CaptureAsync(string label, CancellationToken cancellationToken)
        {
            string tempIndex = Path.Combine(Path.GetTempPath(), "mux-idx-" + Guid.NewGuid().ToString("N"));
            Dictionary<string, string> env = new Dictionary<string, string>
            {
                { "GIT_INDEX_FILE", tempIndex }
            };

            try
            {
                // Stage every tracked and untracked (non-ignored) file into the throwaway index so the
                // snapshot tree mirrors the working tree exactly.
                GitResult add = await RunGitAsync(new[] { "add", "-A" }, env, cancellationToken).ConfigureAwait(false);
                if (add.ExitCode != 0)
                {
                    throw new InvalidOperationException("git add failed while capturing a checkpoint: " + add.StdErr.Trim());
                }

                GitResult writeTree = await RunGitAsync(new[] { "write-tree" }, env, cancellationToken).ConfigureAwait(false);
                if (writeTree.ExitCode != 0)
                {
                    throw new InvalidOperationException("git write-tree failed while capturing a checkpoint: " + writeTree.StdErr.Trim());
                }

                string tree = writeTree.StdOut.Trim();

                // Parent the snapshot on HEAD when the repo has any commits; a fresh repo has no HEAD.
                GitResult head = await RunGitAsync(new[] { "rev-parse", "-q", "--verify", "HEAD" }, null, cancellationToken).ConfigureAwait(false);
                string headSha = head.ExitCode == 0 ? head.StdOut.Trim() : string.Empty;

                List<string> commitArgs = new List<string> { "commit-tree", tree };
                if (!string.IsNullOrEmpty(headSha))
                {
                    commitArgs.Add("-p");
                    commitArgs.Add(headSha);
                }

                commitArgs.Add("-m");
                commitArgs.Add(string.IsNullOrWhiteSpace(label) ? "mux checkpoint" : "mux checkpoint: " + label);

                // Supply an identity so commit-tree succeeds even when the repo has no user.name/email set.
                Dictionary<string, string> identity = new Dictionary<string, string>
                {
                    { "GIT_AUTHOR_NAME", "mux" },
                    { "GIT_AUTHOR_EMAIL", "mux@localhost" },
                    { "GIT_COMMITTER_NAME", "mux" },
                    { "GIT_COMMITTER_EMAIL", "mux@localhost" }
                };

                GitResult commit = await RunGitAsync(commitArgs.ToArray(), identity, cancellationToken).ConfigureAwait(false);
                if (commit.ExitCode != 0)
                {
                    throw new InvalidOperationException("git commit-tree failed while capturing a checkpoint: " + commit.StdErr.Trim());
                }

                return commit.StdOut.Trim();
            }
            finally
            {
                TryDeleteFile(tempIndex);
            }
        }

        /// <summary>
        /// Restores the working tree to a previously captured snapshot: reverts modified and deleted files
        /// to the snapshot's content and removes files created after the snapshot. The real index is reset
        /// to match. Files under <c>.gitignore</c> and effects outside the work tree are not touched.
        /// </summary>
        /// <param name="snapshotSha">The snapshot commit id returned by <see cref="CaptureAsync"/>.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="snapshotSha"/> is null or empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a git command fails.</exception>
        public async Task RestoreAsync(string snapshotSha, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(snapshotSha))
            {
                throw new ArgumentException("Snapshot SHA cannot be null or empty.", nameof(snapshotSha));
            }

            // Delete files present now but absent from the snapshot, so a restore also undoes file creation.
            HashSet<string> current = await ListCurrentFilesAsync(cancellationToken).ConfigureAwait(false);
            HashSet<string> snapshot = await ListSnapshotFilesAsync(snapshotSha, cancellationToken).ConfigureAwait(false);
            foreach (string path in current)
            {
                if (!snapshot.Contains(path))
                {
                    TryDeleteFile(Path.Combine(_WorkingDirectory, path));
                }
            }

            // Reset the index to the snapshot tree, then write every entry back to the working tree.
            GitResult readTree = await RunGitAsync(new[] { "read-tree", snapshotSha }, null, cancellationToken).ConfigureAwait(false);
            if (readTree.ExitCode != 0)
            {
                throw new InvalidOperationException("git read-tree failed while restoring a checkpoint: " + readTree.StdErr.Trim());
            }

            GitResult checkout = await RunGitAsync(new[] { "checkout-index", "-a", "-f" }, null, cancellationToken).ConfigureAwait(false);
            if (checkout.ExitCode != 0)
            {
                throw new InvalidOperationException("git checkout-index failed while restoring a checkpoint: " + checkout.StdErr.Trim());
            }
        }

        #endregion

        #region Private-Methods

        private async Task<HashSet<string>> ListCurrentFilesAsync(CancellationToken cancellationToken)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);

            GitResult tracked = await RunGitAsync(new[] { "ls-files" }, null, cancellationToken).ConfigureAwait(false);
            AddLines(files, tracked.StdOut);

            GitResult untracked = await RunGitAsync(new[] { "ls-files", "--others", "--exclude-standard" }, null, cancellationToken).ConfigureAwait(false);
            AddLines(files, untracked.StdOut);

            return files;
        }

        private async Task<HashSet<string>> ListSnapshotFilesAsync(string snapshotSha, CancellationToken cancellationToken)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.Ordinal);
            GitResult result = await RunGitAsync(new[] { "ls-tree", "-r", "--name-only", snapshotSha }, null, cancellationToken).ConfigureAwait(false);
            AddLines(files, result.StdOut);
            return files;
        }

        private static void AddLines(HashSet<string> set, string output)
        {
            foreach (string raw in output.Split('\n'))
            {
                string line = raw.Trim('\r', ' ', '\t');
                if (line.Length > 0)
                {
                    set.Add(line);
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private async Task<GitResult> RunGitAsync(string[] args, IReadOnlyDictionary<string, string>? env, CancellationToken cancellationToken)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = _WorkingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            if (env != null)
            {
                foreach (KeyValuePair<string, string> pair in env)
                {
                    startInfo.Environment[pair.Key] = pair.Value;
                }
            }

            using Process process = new Process { StartInfo = startInfo };

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) stdout.Append(e.Data).Append('\n'); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) stderr.Append(e.Data).Append('\n'); };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                // git not installed / not on PATH.
                return new GitResult(127, string.Empty, ex.Message);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return new GitResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }

        private readonly struct GitResult
        {
            public GitResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut ?? string.Empty;
                StdErr = stdErr ?? string.Empty;
            }

            public int ExitCode { get; }

            public string StdOut { get; }

            public string StdErr { get; }
        }

        #endregion
    }
}
