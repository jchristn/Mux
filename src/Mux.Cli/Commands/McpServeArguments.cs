namespace Mux.Cli.Commands
{
    using Mux.Core.Enums;

    /// <summary>
    /// The parsed arguments of <c>mux mcp serve</c>.
    /// </summary>
    public sealed class McpServeArguments
    {
        #region Public-Members

        /// <summary>The HTTP port, or null to serve over stdio.</summary>
        public int? HttpPort { get; set; }

        /// <summary>The HTTP host name. Defaults to <c>localhost</c>.</summary>
        public string Host { get; set; } = "localhost";

        /// <summary>The bearer key HTTP clients must send, or null for none (falls back to the <c>mcpServeApiKey</c> setting).</summary>
        public string? ApiKey { get; set; }

        /// <summary>Whether <c>run_skill</c> is registered.</summary>
        public bool AllowSkills { get; set; }

        /// <summary>The most permissive approval policy a <c>run</c> call may use. Defaults to deny.</summary>
        public ApprovalPolicyEnum MaxApprovalPolicy { get; set; } = ApprovalPolicyEnum.Deny;

        /// <summary>The default endpoint for <c>run</c>, or null for the configured default.</summary>
        public string? Endpoint { get; set; }

        /// <summary>The default working directory, or null for the current directory.</summary>
        public string? WorkingDirectory { get; set; }

        #endregion
    }
}
