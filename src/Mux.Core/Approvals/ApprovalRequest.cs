namespace Mux.Core.Approvals
{
    using System;
    using Mux.Core.Tools;

    /// <summary>
    /// Describes a single tool call awaiting approval, carrying the context an escalation UI needs to
    /// present the request and the classification the router uses to decide whether to auto-approve.
    /// </summary>
    public class ApprovalRequest
    {
        #region Private-Members

        private string _JobId = "primary";
        private string _ToolCallId = string.Empty;
        private string _ToolName = string.Empty;
        private string _ArgumentsSummary = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// The id of the job requesting approval.
        /// </summary>
        public string JobId
        {
            get => _JobId;
            set => _JobId = string.IsNullOrEmpty(value) ? "primary" : value;
        }

        /// <summary>
        /// The unique identifier of the tool call.
        /// </summary>
        public string ToolCallId
        {
            get => _ToolCallId;
            set => _ToolCallId = value ?? string.Empty;
        }

        /// <summary>
        /// The name of the tool being called.
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? string.Empty;
        }

        /// <summary>
        /// A summary of the tool's arguments for display (typically the raw arguments JSON).
        /// </summary>
        public string ArgumentsSummary
        {
            get => _ArgumentsSummary;
            set => _ArgumentsSummary = value ?? string.Empty;
        }

        /// <summary>
        /// An optional human-readable diff to show for edit-style tools; null when not applicable.
        /// </summary>
        public string? Diff { get; set; }

        /// <summary>
        /// The mutation classification of the tool, used by the AutoSafe policy to auto-approve
        /// read-only tools and escalate mutating ones.
        /// </summary>
        public ToolMutationKind MutationKind { get; set; } = ToolMutationKind.Mutating;

        #endregion
    }
}
