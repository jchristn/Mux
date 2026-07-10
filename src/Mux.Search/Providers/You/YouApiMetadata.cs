namespace Mux.Search.Providers.You
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed You.com API metadata.
    /// </summary>
    public class YouApiMetadata
    {
        /// <summary>
        /// Gets or sets the search identifier.
        /// </summary>
        [JsonPropertyName("search_uuid")]
        public string? SearchUuid { get; set; }

        /// <summary>
        /// Gets or sets latency in seconds.
        /// </summary>
        [JsonPropertyName("latency")]
        public double? Latency { get; set; }
    }
}
