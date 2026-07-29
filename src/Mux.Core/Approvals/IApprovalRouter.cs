namespace Mux.Core.Approvals
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Resolves whether a tool call may proceed under a per-job approval policy, escalating to the
    /// user only when the policy requires it. Implementations are per-job, so concurrent jobs resolve
    /// approvals independently.
    /// </summary>
    public interface IApprovalRouter
    {
        /// <summary>
        /// Determines whether a tool call is approved. The policy is applied first (auto-approve,
        /// deny, or classification-based auto-safe); when the policy calls for a prompt, and the
        /// decision is not already remembered, <paramref name="escalate"/> is invoked.
        /// </summary>
        /// <param name="request">The tool call requesting approval. Must not be null.</param>
        /// <param name="escalate">
        /// The escalation callback invoked when the user must decide. Must not be null; it is only
        /// called when the policy requires a prompt and no prior "always" grant applies.
        /// </param>
        /// <param name="cancellationToken">A token to cancel the approval wait.</param>
        /// <returns>True if the tool call is approved; otherwise false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> or <paramref name="escalate"/> is null.</exception>
        Task<bool> RequestApprovalAsync(
            ApprovalRequest request,
            Func<ApprovalRequest, CancellationToken, Task<ApprovalDecision>> escalate,
            CancellationToken cancellationToken);

        /// <summary>
        /// Promotes this router to auto-approve all subsequent tool calls for the rest of the job.
        /// </summary>
        void PromoteToAutoApprove();
    }
}
