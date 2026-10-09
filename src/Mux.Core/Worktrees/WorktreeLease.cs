namespace Mux.Core.Worktrees
{
    using System;

    /// <summary>
    /// An isolated git worktree created for one run: where it lives, which branch it is on, and the commit it
    /// started from. Returned by <see cref="WorktreeManager.CreateAsync"/> and passed back to
    /// <see cref="WorktreeManager.FinishAsync"/> when the run ends.
    /// </summary>
    public sealed class WorktreeLease
    {
        #region Public-Members

        /// <summary>The short name of the worktree (its directory name), for example <c>subagent-reviewer</c>.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What kind of run owns it: <c>subagent</c> or <c>job</c>.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>The run label (the subagent name or job id).</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>The main working tree's top-level directory.</summary>
        public string RepositoryRoot { get; set; } = string.Empty;

        /// <summary>The worktree's top-level directory.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// The directory the run should use: the worktree path plus the same subdirectory the caller started in.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>The new branch the worktree is on (<c>mux/&lt;kind&gt;/&lt;label&gt;</c>).</summary>
        public string Branch { get; set; } = string.Empty;

        /// <summary>The commit the worktree started from (the main tree's HEAD at creation).</summary>
        public string BaseCommit { get; set; } = string.Empty;

        /// <summary>
        /// Whether the main working tree had uncommitted changes at creation. Those changes are not in the worktree,
        /// which always starts from HEAD.
        /// </summary>
        public bool BaseDirty { get; set; }

        /// <summary>When the worktree was created (UTC).</summary>
        public DateTime CreatedUtc { get; set; }

        #endregion
    }
}
