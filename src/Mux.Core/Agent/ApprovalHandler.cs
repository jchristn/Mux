namespace Mux.Core.Agent
{
    using System;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Handles tool call approval based on the configured approval policy.
    /// </summary>
    public class ApprovalHandler
    {
        #region Private-Members

        private ApprovalPolicyEnum _Policy;
        private bool _PromotedToAutoApprove = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ApprovalHandler"/> class.
        /// </summary>
        /// <param name="policy">The approval policy to enforce.</param>
        public ApprovalHandler(ApprovalPolicyEnum policy)
        {
            _Policy = policy;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Requests approval for the specified tool call according to the configured policy.
        /// </summary>
        /// <param name="toolCall">The tool call requiring approval.</param>
        /// <param name="promptUserFunc">
        /// A callback that prompts the user and returns their response string.
        /// </param>
        /// <returns>True if the tool call is approved; otherwise false.</returns>
        public async Task<bool> RequestApprovalAsync(
            ToolCall toolCall,
            Func<ToolCall, Task<string>> promptUserFunc)
        {
            if (toolCall == null) throw new ArgumentNullException(nameof(toolCall));
            if (promptUserFunc == null) throw new ArgumentNullException(nameof(promptUserFunc));

            if (_Policy == ApprovalPolicyEnum.AutoApprove)
                return true;

            if (_Policy == ApprovalPolicyEnum.Deny)
                return false;

            // Policy is Ask
            if (_PromotedToAutoApprove)
                return true;

            string response = await promptUserFunc(toolCall).ConfigureAwait(false);
            string trimmed = (response ?? string.Empty).Trim();

            if (string.Equals(trimmed, "always", StringComparison.OrdinalIgnoreCase))
            {
                _PromotedToAutoApprove = true;
                return true;
            }

            if (string.Equals(trimmed, "n", StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Y, yes, or empty all approve
            return true;
        }

        /// <summary>
        /// Promotes this handler to auto-approve all subsequent tool calls.
        /// </summary>
        public void PromoteToAutoApprove()
        {
            _PromotedToAutoApprove = true;
        }

        #endregion
    }
}
