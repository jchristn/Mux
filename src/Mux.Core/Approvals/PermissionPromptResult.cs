namespace Mux.Core.Approvals
{
    /// <summary>
    /// The answer of an external permission prompt (for example an MCP permission prompt tool) for one tool call.
    /// </summary>
    public class PermissionPromptResult
    {
        #region Public-Members

        /// <summary>
        /// True when the tool call may run.
        /// </summary>
        public bool Approved { get; set; } = false;

        /// <summary>
        /// Why the call was denied, returned to the model as the tool result; null when approved or when no reason was given.
        /// </summary>
        public string? Message { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// An approval.
        /// </summary>
        /// <returns>The result.</returns>
        public static PermissionPromptResult Allow()
        {
            return new PermissionPromptResult { Approved = true };
        }

        /// <summary>
        /// A denial.
        /// </summary>
        /// <param name="message">Reason returned to the model, or null.</param>
        /// <returns>The result.</returns>
        public static PermissionPromptResult Deny(string? message)
        {
            return new PermissionPromptResult { Approved = false, Message = string.IsNullOrWhiteSpace(message) ? null : message!.Trim() };
        }

        #endregion
    }
}
