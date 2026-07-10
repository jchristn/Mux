namespace Mux.Search.Providers.Tavily
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed Tavily auto-parameters metadata.
    /// </summary>
    public class TavilyApiAutoParameters
    {
        /// <summary>
        /// Gets or sets the selected topic.
        /// </summary>
        [JsonPropertyName("topic")]
        public string? Topic { get; set; }

        /// <summary>
        /// Gets or sets the selected search depth.
        /// </summary>
        [JsonPropertyName("search_depth")]
        public string? SearchDepth { get; set; }
    }
}
