namespace Mux.Cli.Rendering
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed subset of a multi-edit entry used for display summaries.
    /// </summary>
    public class ToolEditSummary
    {
        /// <summary>
        /// Gets or sets the text searched by the edit.
        /// </summary>
        [JsonPropertyName("old_string")]
        public string? OldString { get; set; }

        /// <summary>
        /// Gets or sets the replacement text.
        /// </summary>
        [JsonPropertyName("new_string")]
        public string? NewString { get; set; }
    }
}
