namespace Mux.Core.Worktrees
{
    using System.Collections.Generic;

    /// <summary>
    /// What an isolated run left behind: whether the worktree was removed (no changes) or kept, and if kept, its
    /// branch, path, commits, and diff summary.
    /// </summary>
    public sealed class WorktreeOutcome
    {
        #region Public-Members

        /// <summary>The worktree name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The worktree path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The branch.</summary>
        public string Branch { get; set; } = string.Empty;

        /// <summary>The commit the worktree started from.</summary>
        public string BaseCommit { get; set; } = string.Empty;

        /// <summary>Whether the run changed anything (committed or not).</summary>
        public bool Changed { get; set; }

        /// <summary>Whether the worktree and its branch were removed because nothing changed.</summary>
        public bool Removed { get; set; }

        /// <summary>Whether uncommitted changes were committed onto the branch when the run ended.</summary>
        public bool CommittedLeftovers { get; set; }

        /// <summary>The commits on the branch since the base, newest first, as <c>sha subject</c> lines.</summary>
        public List<string> Commits { get; set; } = new List<string>();

        /// <summary>The <c>git diff --stat</c> summary of the branch against its base.</summary>
        public string DiffStat { get; set; } = string.Empty;

        /// <summary>A problem that did not stop the run (for example a cleanup step that failed), or null.</summary>
        public string? Warning { get; set; }

        #endregion
    }
}
