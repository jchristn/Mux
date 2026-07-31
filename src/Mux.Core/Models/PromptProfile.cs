namespace Mux.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A named, switchable set of the prompts mux sends to the model. Persisted in
    /// <c>~/.mux/prompts.json</c>. Any field left empty falls back to the built-in default for that prompt,
    /// so a profile only needs to carry the prompts it customizes. Exactly one profile is active at a time;
    /// the active profile's prompts are applied to every turn (its system prompt keeps the
    /// <c>{WorkingDirectory}</c> and <c>{ToolDescriptions}</c> placeholders, which are substituted at run
    /// time).
    /// </summary>
    public class PromptProfile
    {
        /// <summary>
        /// The profile's unique display name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is the active profile whose prompts are applied to turns.
        /// </summary>
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        /// <summary>
        /// The main system prompt (persona) sent with every turn. Keeps the <c>{WorkingDirectory}</c> and
        /// <c>{ToolDescriptions}</c> placeholders. Empty falls back to the built-in default.
        /// </summary>
        [JsonPropertyName("systemPrompt")]
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// The system prompt used instead of <see cref="SystemPrompt"/> when the active endpoint does not
        /// support tools. Keeps the <c>{WorkingDirectory}</c> placeholder. Empty falls back to the built-in
        /// default.
        /// </summary>
        [JsonPropertyName("toolsDisabledPrompt")]
        public string ToolsDisabledPrompt { get; set; } = string.Empty;

        /// <summary>
        /// The system prompt for the automatic history-compaction sidecar call. Empty falls back to the
        /// built-in default.
        /// </summary>
        [JsonPropertyName("compactionPrompt")]
        public string CompactionPrompt { get; set; } = string.Empty;
    }
}
