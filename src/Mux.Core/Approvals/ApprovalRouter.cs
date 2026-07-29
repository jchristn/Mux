namespace Mux.Core.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Tools;

    /// <summary>
    /// Default per-job approval router. Applies the configured <see cref="ApprovalPolicyEnum"/> and,
    /// when a prompt is required, escalates to a caller-supplied callback and remembers "always"
    /// grants for the life of the router. Thread-safe; intended to be created once per job so
    /// concurrent jobs resolve approvals independently.
    /// </summary>
    public sealed class ApprovalRouter : IApprovalRouter
    {
        #region Private-Members

        private readonly object _SyncRoot = new object();
        private readonly ApprovalPolicyEnum _Policy;
        private readonly HashSet<string> _AlwaysApprovedTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _AutoSafeAllowlist = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _AlwaysApproveSession = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ApprovalRouter"/> class.
        /// </summary>
        /// <param name="policy">The approval policy to enforce.</param>
        /// <param name="autoSafeAllowlist">
        /// Optional tool names that <see cref="ApprovalPolicyEnum.AutoSafe"/> auto-approves in addition
        /// to read-only tools. Ignored under other policies.
        /// </param>
        public ApprovalRouter(ApprovalPolicyEnum policy, IEnumerable<string>? autoSafeAllowlist = null)
        {
            _Policy = policy;

            if (autoSafeAllowlist != null)
            {
                foreach (string toolName in autoSafeAllowlist)
                {
                    if (!string.IsNullOrEmpty(toolName))
                    {
                        _AutoSafeAllowlist.Add(toolName);
                    }
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public async Task<bool> RequestApprovalAsync(
            ApprovalRequest request,
            Func<ApprovalRequest, CancellationToken, Task<ApprovalDecision>> escalate,
            CancellationToken cancellationToken)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (escalate is null) throw new ArgumentNullException(nameof(escalate));

            cancellationToken.ThrowIfCancellationRequested();

            // Remembered "always" grants apply regardless of policy (except Deny, handled below).
            lock (_SyncRoot)
            {
                if (_Policy == ApprovalPolicyEnum.Deny)
                {
                    return false;
                }

                if (_Policy == ApprovalPolicyEnum.AutoApprove)
                {
                    return true;
                }

                if (_AlwaysApproveSession || _AlwaysApprovedTools.Contains(request.ToolName))
                {
                    return true;
                }

                // AutoSafe auto-approves read-only tools and any allowlisted tool.
                if (_Policy == ApprovalPolicyEnum.AutoSafe
                    && (request.MutationKind == ToolMutationKind.ReadOnly || _AutoSafeAllowlist.Contains(request.ToolName)))
                {
                    return true;
                }
            }

            // Policy requires a prompt (Ask, or AutoSafe on a mutating/unclassified tool).
            ApprovalDecision decision = await escalate(request, cancellationToken).ConfigureAwait(false);

            lock (_SyncRoot)
            {
                switch (decision)
                {
                    case ApprovalDecision.AlwaysThisSession:
                        _AlwaysApproveSession = true;
                        return true;
                    case ApprovalDecision.AlwaysThisTool:
                        _AlwaysApprovedTools.Add(request.ToolName);
                        return true;
                    case ApprovalDecision.Approved:
                        return true;
                    default:
                        return false;
                }
            }
        }

        /// <inheritdoc/>
        public void PromoteToAutoApprove()
        {
            lock (_SyncRoot)
            {
                _AlwaysApproveSession = true;
            }
        }

        #endregion
    }
}
