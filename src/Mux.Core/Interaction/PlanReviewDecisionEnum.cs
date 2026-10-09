namespace Mux.Core.Interaction
{
    /// <summary>
    /// What the user decided about a plan presented with <c>exit_plan</c>.
    /// </summary>
    public enum PlanReviewDecisionEnum
    {
        /// <summary>Approve the plan and run it with edits auto-approved.</summary>
        ApproveAutoAccept = 0,

        /// <summary>Approve the plan and run it with the session's normal approvals.</summary>
        Approve = 1,

        /// <summary>Keep planning; the feedback goes back to the model.</summary>
        KeepPlanning = 2
    }
}
