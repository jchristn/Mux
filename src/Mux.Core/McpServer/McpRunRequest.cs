namespace Mux.Core.McpServer
{
    using Mux.Core.Enums;

    /// <summary>
    /// One <c>run</c> tool call, after validation: the prompt and the effective settings for the agent turn.
    /// </summary>
    public sealed class McpRunRequest
    {
        #region Public-Members

        /// <summary>The prompt to run. Never blank once validated.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>The endpoint name, or null for the server's default.</summary>
        public string? Endpoint { get; set; }

        /// <summary>The working directory for the turn (absolute).</summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>The approval policy, already capped by the server's ceiling.</summary>
        public ApprovalPolicyEnum ApprovalPolicy { get; set; } = ApprovalPolicyEnum.Deny;

        /// <summary>The iteration cap requested by the caller, or null for the configured default.</summary>
        public int? MaxTurns { get; set; }

        #endregion
    }
}
