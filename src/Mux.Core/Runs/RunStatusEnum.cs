namespace Mux.Core.Runs
{
    /// <summary>
    /// Lifecycle status of a server-hosted agent run tracked by the <see cref="RunRegistry"/>.
    /// </summary>
    public enum RunStatusEnum
    {
        /// <summary>The run is executing.</summary>
        Running,

        /// <summary>The run is paused waiting for a tool-call approval decision.</summary>
        AwaitingApproval,

        /// <summary>The run finished normally (including "completed with errors").</summary>
        Completed,

        /// <summary>The run ended because of a fatal error.</summary>
        Failed,

        /// <summary>The run was canceled by an explicit cancel request or client disconnect.</summary>
        Canceled
    }
}
