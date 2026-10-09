namespace Mux.Core.Enums
{
    /// <summary>
    /// Where a delegated run (a subagent or a job) does its work.
    /// </summary>
    public enum IsolationModeEnum
    {
        /// <summary>
        /// In the shared working tree, serialized with other runs through the workspace write lease. The default.
        /// </summary>
        None = 0,

        /// <summary>
        /// In its own git worktree on a new <c>mux/&lt;kind&gt;/&lt;id&gt;</c> branch, without the shared write lease.
        /// </summary>
        Worktree = 1
    }
}
