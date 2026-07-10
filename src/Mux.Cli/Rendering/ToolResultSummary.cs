namespace Mux.Cli.Rendering
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed subset of a tool result used for display summaries.
    /// </summary>
    public class ToolResultSummary
    {
        /// <summary>
        /// Gets or sets whether the tool completed successfully.
        /// </summary>
        [JsonPropertyName("success")]
        public bool? Success { get; set; }

        /// <summary>
        /// Gets or sets the file path returned by file tools.
        /// </summary>
        [JsonPropertyName("file_path")]
        public string? FilePath { get; set; }

        /// <summary>
        /// Gets or sets the directory path returned by directory tools.
        /// </summary>
        [JsonPropertyName("path")]
        public string? Path { get; set; }

        /// <summary>
        /// Gets or sets a line count returned by file tools.
        /// </summary>
        [JsonPropertyName("line_count")]
        public int? LineCount { get; set; }

        /// <summary>
        /// Gets or sets an edit count returned by edit tools.
        /// </summary>
        [JsonPropertyName("edits_applied")]
        public int? EditsApplied { get; set; }

        /// <summary>
        /// Gets or sets an error message.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>
        /// Gets or sets a result message.
        /// </summary>
        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
