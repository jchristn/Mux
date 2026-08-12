namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The reasoning effort level requested of a reasoning-capable model. Selecting a level drives
    /// provider-specific defaults (OpenAI <c>reasoning_effort</c>, Gemini thinking budget, Ollama
    /// <c>think</c>); the absence of a level is modeled as a null <see cref="Mux.Core.Models.ReasoningEffortConfig"/>
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
        High
    }
}
