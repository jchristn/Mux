namespace Mux.Core.Worktrees
{
    using System;

    /// <summary>
    /// One mux-owned worktree as listed by <see cref="WorktreeManager.ListAsync"/>.
    /// </summary>
    public sealed class WorktreeInfo
    {
        #region Public-Members

        /// <summary>The worktree name (its directory name).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The worktree path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The branch, or empty when detached.</summary>
        public string Branch { get; set; } = string.Empty;

        /// <summary>The commit the worktree is on.</summary>
        public string Head { get; set; } = string.Empty;

        /// <summary>The commit it started from, when known.</summary>
        public string BaseCommit { get; set; } = string.Empty;

        /// <summary>The kind of run that created it, when known.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>When it was created (UTC), when known.</summary>
        public DateTime? CreatedUtc { get; set; }

        /// <summary>Whether its directory still exists.</summary>
        public bool Exists { get; set; }

        /// <summary>Whether it has uncommitted changes.</summary>
        public bool Dirty { get; set; }

        /// <summary>Commits on its branch since the base, or -1 when the base is unknown.</summary>
        public int CommitsAhead { get; set; } = -1;

        #endregion
    }
}
