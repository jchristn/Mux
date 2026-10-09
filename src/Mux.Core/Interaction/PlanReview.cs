namespace Mux.Core.Interaction
{
    /// <summary>
    /// The user's review of a <see cref="PlanProposal"/>.
    /// </summary>
    public sealed class PlanReview
    {
        #region Public-Members

        /// <summary>The decision.</summary>
        public PlanReviewDecisionEnum Decision { get; set; } = PlanReviewDecisionEnum.KeepPlanning;

        /// <summary>Feedback for the model when the user keeps planning, or empty.</summary>
        public string Feedback { get; set; } = string.Empty;

        /// <summary>Whether the plan was approved (with or without auto-accept).</summary>
        public bool Approved => Decision == PlanReviewDecisionEnum.Approve || Decision == PlanReviewDecisionEnum.ApproveAutoAccept;

        #endregion
    }
}
