namespace Mux.Core.Approvals
{
    /// <summary>
    /// The outcome of an approval escalation to the user for a single tool call.
    /// </summary>
    public enum ApprovalDecision
    {
        /// <summary>
        /// Approve this tool call only.
        /// </summary>
        Approved = 0,

        /// <summary>
        /// Deny this tool call.
        /// </summary>
        Denied = 1,

        /// <summary>
        /// Approve this call and auto-approve all subsequent tool calls for the rest of the session/job.
        /// </summary>
        AlwaysThisSession = 2,

        /// <summary>
        /// Approve this call and auto-approve all subsequent calls to the same tool for the session/job.
        /// </summary>
        AlwaysThisTool = 3
    }
}
