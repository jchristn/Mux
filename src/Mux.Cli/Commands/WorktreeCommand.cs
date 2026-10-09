namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Worktrees;

    /// <summary>
    /// Implements <c>mux worktree list|prune|remove</c>: manages the git worktrees that isolated subagents and jobs
    /// leave behind when they changed something. It only ever touches mux-owned worktrees and <c>mux/</c> branches.
    /// </summary>
    public sealed class WorktreeCommand
    {
        #region Private-Members

        private const string Usage = "Usage: mux worktree list|prune|remove <name> [--force] [--keep-branch] [--cwd <dir>] [--output-format json]";

        private readonly WorktreeManager _Manager;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="WorktreeCommand"/> class.
        /// </summary>
        /// <param name="manager">The worktree manager, or null for a default one.</param>
        public WorktreeCommand(WorktreeManager? manager = null)
        {
            _Manager = manager ?? new WorktreeManager();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs the verb.
        /// </summary>
        /// <param name="args">Arguments after <c>worktree</c>.</param>
        /// <param name="cancellationToken">Cancels the git commands.</param>
        /// <returns>0 on success, 1 on an error, 2 on bad usage.</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            string action = "list";
            string target = string.Empty;
            string directory = Directory.GetCurrentDirectory();
            bool force = false;
            bool keepBranch = false;
            bool json = false;
            List<string> positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "--force") force = true;
                else if (arg == "--keep-branch") keepBranch = true;
                else if ((arg == "--cwd" || arg == "-w" || arg == "--working-directory") && i + 1 < args.Length) directory = args[++i];
                else if (arg == "--output-format" && i + 1 < args.Length) json = string.Equals(args[++i], "json", StringComparison.OrdinalIgnoreCase);
                else if (arg.StartsWith("-", StringComparison.Ordinal))
                {
                    Console.Error.WriteLine("Unknown option '" + arg + "'. " + Usage);
                    return 2;
                }
                else positional.Add(arg);
            }

            if (positional.Count > 0) action = positional[0].ToLowerInvariant();
            if (positional.Count > 1) target = positional[1];

            try
            {
                switch (action)
                {
                    case "list":
                    case "ls":
                        return await ListAsync(directory, json, cancellationToken).ConfigureAwait(false);
                    case "prune":
                        IReadOnlyList<string> pruned = await _Manager.PruneAsync(directory, cancellationToken).ConfigureAwait(false);
                        if (json)
                        {
                            Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, pruned }));
                        }
                        else
                        {
                            Console.WriteLine(pruned.Count == 0 ? "Nothing to prune." : "Pruned " + pruned.Count + ": " + string.Join(", ", pruned));
                        }

                        return 0;
                    case "remove":
                    case "rm":
                        if (string.IsNullOrWhiteSpace(target))
                        {
                            Console.Error.WriteLine(Usage);
                            return 2;
                        }

                        WorktreeInfo removed = await _Manager.RemoveAsync(directory, target, force, keepBranch, cancellationToken).ConfigureAwait(false);
                        if (json)
                        {
                            Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, removed = removed.Name, branch = removed.Branch, branchKept = keepBranch }));
                        }
                        else
                        {
                            Console.WriteLine("Removed worktree " + removed.Name + (keepBranch ? " (kept branch " + removed.Branch + ")" : " and branch " + removed.Branch) + ".");
                        }

                        return 0;
                    default:
                        Console.Error.WriteLine(Usage);
                        return 2;
                }
            }
            catch (InvalidOperationException ex)
            {
                if (json)
                {
                    Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = false, error = ex.Message }));
                }
                else
                {
                    Console.Error.WriteLine(ex.Message);
                }

                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private async Task<int> ListAsync(string directory, bool json, CancellationToken cancellationToken)
        {
            IReadOnlyList<WorktreeInfo> worktrees = await _Manager.ListAsync(directory, cancellationToken).ConfigureAwait(false);
            if (json)
            {
                List<object> items = new List<object>();
                foreach (WorktreeInfo info in worktrees)
                {
                    items.Add(new { name = info.Name, path = info.Path, branch = info.Branch, kind = info.Kind, exists = info.Exists, dirty = info.Dirty, commitsAhead = info.CommitsAhead, baseCommit = info.BaseCommit });
                }

                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, worktrees = items }));
                return 0;
            }

            if (worktrees.Count == 0)
            {
                Console.WriteLine("No mux worktrees.");
                return 0;
            }

            foreach (WorktreeInfo info in worktrees)
            {
                Console.WriteLine(Describe(info));
            }

            return 0;
        }

        /// <summary>
        /// Describes one worktree on a line: name, branch, state, and path.
        /// </summary>
        /// <param name="info">The worktree.</param>
        /// <returns>The line.</returns>
        public static string Describe(WorktreeInfo info)
        {
            if (info == null) throw new ArgumentNullException(nameof(info));

            List<string> state = new List<string>();
            if (!info.Exists) state.Add("missing");
            if (info.Dirty) state.Add("uncommitted changes");
            if (info.CommitsAhead > 0) state.Add(info.CommitsAhead + " commit(s)");
            if (info.Exists && !info.Dirty && info.CommitsAhead == 0) state.Add("no changes");
            return info.Name + "  " + (info.Branch.Length > 0 ? info.Branch : "(detached)") + "  " + string.Join(", ", state) + "  " + info.Path;
        }

        #endregion
    }
}
