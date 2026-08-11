namespace Mux.Core.Enums
{
    /// <summary>
    /// The application-level confinement posture applied to tool execution. This is a mux-enforced policy
    /// over the built-in tools, not an operating-system sandbox: it governs which built-in tools may run
    /// and where the built-in file tools may write. Arbitrary subprocesses started by <c>run_process</c>
    /// or by external MCP servers are not OS-sandboxed by this posture and remain gated only by the
    /// approval policy.
    /// </summary>
    public enum SandboxPostureEnum
    {
        /// <summary>
        /// No confinement (the default). Tool availability is governed only by the approval policy and any
        /// allow/deny tool lists.
        /// </summary>
        None = 0,

        /// <summary>
        /// Read-only: every tool classified as mutating (per <see cref="Mux.Core.Tools.ToolMutationKind"/>)
        /// is refused before execution, so the run can inspect the workspace but cannot change it.
        /// </summary>
        ReadOnly = 1,

        /// <summary>
        /// Workspace-write: built-in file-mutating tools may only write within the working directory and any
        /// additional allowed roots; a write whose resolved path escapes those roots is refused. Reads are
        /// unrestricted and <c>run_process</c> remains allowed under the approval policy.
        /// </summary>
        WorkspaceWrite = 2
    }
}
