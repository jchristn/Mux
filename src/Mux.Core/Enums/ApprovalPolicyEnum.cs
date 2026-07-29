namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Policy that governs whether a tool call requires user approval.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ApprovalPolicyEnum
    {
        /// <summary>
        /// Prompt the user for approval before executing.
        /// </summary>
        [EnumMember(Value = "ask")]
        Ask = 0,

        /// <summary>
        /// Execute automatically without prompting.
        /// </summary>
        [EnumMember(Value = "auto_approve")]
        AutoApprove = 1,

        /// <summary>
        /// Deny execution unconditionally.
        /// </summary>
        [EnumMember(Value = "deny")]
        Deny = 2,

        /// <summary>
        /// Automatically approve read-only tools (and any per-tool allowlist), and prompt for approval
        /// on mutating or otherwise unclassified tools. Intended for the concurrent multi-job UI.
        /// </summary>
        [EnumMember(Value = "auto_safe")]
        AutoSafe = 3
    }
}
