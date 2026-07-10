namespace Mux.Search.Providers.Tavily
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed Tavily usage metadata.
    /// </summary>
    public class TavilyApiUsage
    {
        /// <summary>
        /// Gets or sets credits used.
        /// </summary>
        [JsonPropertyName("credits_used")]
        public int? CreditsUsed { get; set; }

        /// <summary>
        /// Gets or sets alternate credits used.
        /// </summary>
        [JsonPropertyName("credits")]
        public int? Credits { get; set; }
    }
}
