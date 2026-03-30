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
        Deny = 2
    }
}
