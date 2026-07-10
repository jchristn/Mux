namespace Mux.Cli.Rendering
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed subset of built-in tool arguments used for display summaries.
    /// </summary>
    public class ToolArgumentSummary
    {
        /// <summary>
        /// Gets or sets the file path argument.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        /// <summary>
        /// Gets or sets the path argument.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets the new path argument.
        /// </summary>
        [JsonPropertyName("new_path")]
        public string? NewPath { get; set; }

        /// <summary>
        /// Gets or sets the directory action.
        /// </summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the glob or grep pattern.
        /// </summary>
        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; }

        /// <summary>
        /// Gets or sets the URL argument.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// Gets or sets the command argument.
        /// </summary>
        [JsonPropertyName("command")]
        public string? Command { get; set; }

        /// <summary>
        /// Gets or sets process arguments.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string>? Args { get; set; }

        /// <summary>
        /// Gets or sets multi-edit entries.
        /// </summary>
        [JsonPropertyName("edits")]
        public List<ToolEditSummary>? Edits { get; set; }
    }
}
