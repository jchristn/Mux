namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The reasoning effort level requested of a reasoning-capable model. Selecting a level drives
    /// provider-specific defaults (OpenAI <c>reasoning.effort</c> or <c>reasoning_effort</c>, Gemini thinking
    /// budget, Ollama <c>think</c>); the absence of a level is modeled as a null <see cref="Mux.Core.Models.ReasoningEffortConfig"/>
    /// or a null level, which sends no reasoning field.
    /// </summary>
    [JsonConverter(typeof(ReasoningLevelEnumConverter))]
    public enum ReasoningLevelEnum
    {
        /// <summary>
        /// Minimal reasoning. Maps to OpenAI <c>minimal</c> and disables extended thinking where a backend
        /// can toggle it off.
        /// </summary>
        [EnumMember(Value = "minimal")]
        Minimal,

        /// <summary>
        /// Low reasoning effort.
        /// </summary>
        [EnumMember(Value = "low")]
        Low,

        /// <summary>
        /// Medium reasoning effort.
        /// </summary>
        [EnumMember(Value = "medium")]
        Medium,

        /// <summary>
        /// High reasoning effort.
        /// </summary>
        [EnumMember(Value = "high")]
        High,

        /// <summary>
        /// Extra-high reasoning effort. Maps to OpenAI and xAI <c>xhigh</c>; providers without it use their
        /// <see cref="High"/> mapping.
        /// </summary>
        [EnumMember(Value = "xhigh")]
        XHigh,

        /// <summary>
        /// Maximum reasoning effort. Maps to OpenAI <c>max</c> and xAI <c>xhigh</c>; other providers use their
        /// <see cref="High"/> mapping.
        /// </summary>
        [EnumMember(Value = "max")]
        Max,

        /// <summary>
        /// Explicitly no reasoning. Unlike leaving the level unset (which sends no reasoning field at all),
        /// this asks the provider to turn reasoning off: OpenAI <c>none</c>, or each provider's way of
        /// disabling thinking. Declared last so the numeric values of the earlier levels are unchanged.
        /// </summary>
        [EnumMember(Value = "none")]
        None
    }
}
