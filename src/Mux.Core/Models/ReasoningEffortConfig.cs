namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using Mux.Core.Enums;

    /// <summary>
    /// Per-endpoint reasoning effort selection. A <see cref="Level"/> supplies provider-appropriate
    /// defaults; the per-provider properties override individual values. A null <see cref="Level"/> (and a
    /// null <see cref="ReasoningEffortConfig"/> on an endpoint) means "send no reasoning field". Serialized
    /// under <c>reasoningEffort</c> in <c>endpoints.json</c>. The projection onto a provider request is
    /// performed at the LLM boundary, not here, so this model carries no dependency on the LLM library.
    /// </summary>
    public class ReasoningEffortConfig
    {
        #region Private-Members

        private ReasoningLevelEnum? _Level = null;
        private string? _OpenAiValue = null;
        private int? _GeminiThinkingBudget = null;
        private string? _OllamaThink = null;

        private const int GeminiThinkingBudgetFloor = -1;
        private const int GeminiThinkingBudgetCeiling = 32768;

        #endregion

        #region Public-Members

        /// <summary>
        /// The semantic effort level. Null sends no reasoning field.
        /// </summary>
        [JsonPropertyName("level")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReasoningLevelEnum? Level
        {
            get => _Level;
            set => _Level = value;
        }

        /// <summary>
        /// Optional OpenAI <c>reasoning_effort</c> override. Null derives from <see cref="Level"/>. Set
        /// values are normalized to one of <c>minimal</c>, <c>low</c>, <c>medium</c>, <c>high</c>; an
        /// unrecognized value reverts to null.
        /// </summary>
        [JsonPropertyName("openAiValue")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OpenAiValue
        {
            get => _OpenAiValue;
            set => _OpenAiValue = NormalizeToken(value, "minimal", "low", "medium", "high");
        }

        /// <summary>
        /// Optional Gemini thinking-token budget override (<c>thinkingConfig.thinkingBudget</c>). Null
        /// derives from <see cref="Level"/>. -1 selects the model's dynamic budget, 0 disables thinking, and
        /// a positive value is an explicit token budget. Clamped to -1..32768.
        /// </summary>
        [JsonPropertyName("geminiThinkingBudget")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? GeminiThinkingBudget
        {
            get => _GeminiThinkingBudget;
            set => _GeminiThinkingBudget = value.HasValue
                ? Math.Clamp(value.Value, GeminiThinkingBudgetFloor, GeminiThinkingBudgetCeiling)
                : null;
        }

        /// <summary>
        /// Optional Ollama <c>think</c> override. Null derives from <see cref="Level"/>. Set values are
        /// normalized to one of <c>low</c>, <c>medium</c>, <c>high</c>, <c>true</c>, <c>false</c>; an
        /// unrecognized value reverts to null.
        /// </summary>
        [JsonPropertyName("ollamaThink")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? OllamaThink
        {
            get => _OllamaThink;
            set => _OllamaThink = NormalizeToken(value, "low", "medium", "high", "true", "false");
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether this configuration would send a reasoning field. True only when a <see cref="Level"/> is
        /// set (the per-provider overrides are inert without an active level).
        /// </summary>
        /// <returns>True when a level is set.</returns>
        public bool IsActive()
        {
            return _Level.HasValue;
        }

        /// <summary>
        /// Returns a deep copy of this configuration.
        /// </summary>
        /// <returns>A new <see cref="ReasoningEffortConfig"/> with the same values.</returns>
        public ReasoningEffortConfig Clone()
        {
            return new ReasoningEffortConfig
            {
                Level = _Level,
                OpenAiValue = _OpenAiValue,
                GeminiThinkingBudget = _GeminiThinkingBudget,
                OllamaThink = _OllamaThink
            };
        }

        /// <summary>
        /// Returns a new configuration with each non-null field of <paramref name="over"/> taking
        /// precedence over this one. A null <paramref name="over"/> returns a clone of this config.
        /// </summary>
        /// <param name="over">The overriding configuration, or null.</param>
        /// <returns>The merged configuration.</returns>
        public ReasoningEffortConfig Merge(ReasoningEffortConfig? over)
        {
            ReasoningEffortConfig merged = Clone();
            if (over == null)
            {
                return merged;
            }

            if (over.Level.HasValue) merged.Level = over.Level;
            if (over.OpenAiValue != null) merged.OpenAiValue = over.OpenAiValue;
            if (over.GeminiThinkingBudget.HasValue) merged.GeminiThinkingBudget = over.GeminiThinkingBudget;
            if (over.OllamaThink != null) merged.OllamaThink = over.OllamaThink;
            return merged;
        }

        #endregion

        #region Private-Methods

        private static string? NormalizeToken(string? value, params string[] allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            string normalized = value.Trim().ToLowerInvariant();
            foreach (string candidate in allowed)
            {
                if (string.Equals(normalized, candidate, StringComparison.Ordinal))
                {
                    return normalized;
                }
            }

            return null;
        }

        #endregion
    }
}
