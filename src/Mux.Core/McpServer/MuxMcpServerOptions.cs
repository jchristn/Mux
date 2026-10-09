namespace Mux.Core.McpServer
{
    using Mux.Core.Enums;

    /// <summary>
    /// How <c>mux mcp serve</c> behaves: which tools it exposes, the most permissive approval policy a <c>run</c> call
    /// may use, and the defaults applied when a call does not say.
    /// </summary>
    public sealed class MuxMcpServerOptions
    {
        #region Private-Members

        private ApprovalPolicyEnum _MaxApprovalPolicy = ApprovalPolicyEnum.Deny;

        #endregion

        #region Public-Members

        /// <summary>The server name reported in the MCP handshake.</summary>
        public string ServerName { get; set; } = "mux";

        /// <summary>The server version reported in the MCP handshake.</summary>
        public string ServerVersion { get; set; } = string.Empty;

        /// <summary>Whether the <c>run_skill</c> tool is registered. Defaults to false.</summary>
        public bool AllowSkills { get; set; }

        /// <summary>
        /// The most permissive approval policy a <c>run</c> call may use: <see cref="ApprovalPolicyEnum.Deny"/> (the
        /// default), <see cref="ApprovalPolicyEnum.AutoSafe"/>, or <see cref="ApprovalPolicyEnum.AutoApprove"/>.
        /// <see cref="ApprovalPolicyEnum.Ask"/> is treated as <see cref="ApprovalPolicyEnum.Deny"/> because no person
        /// is attached to an MCP server.
        /// </summary>
        public ApprovalPolicyEnum MaxApprovalPolicy
        {
            get => _MaxApprovalPolicy;
            set => _MaxApprovalPolicy = value == ApprovalPolicyEnum.Ask ? ApprovalPolicyEnum.Deny : value;
        }

        /// <summary>The endpoint used when a <c>run</c> call names none, or null for the configured default.</summary>
        public string? DefaultEndpoint { get; set; }

        /// <summary>The working directory used when a call names none, or null for the process directory.</summary>
        public string? DefaultWorkingDirectory { get; set; }

        /// <summary>The session store directory read by the session tools, or null for the default.</summary>
        public string? SessionsDirectory { get; set; }

        #endregion
    }
}
