namespace Mux.Search.Providers.You
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed You.com API response body.
    /// </summary>
    public class YouApiResponse
    {
        /// <summary>
        /// Gets or sets grouped result sections.
        /// </summary>
        [JsonPropertyName("results")]
        public YouApiResults? Results { get; set; }

        /// <summary>
        /// Gets or sets response metadata.
        /// </summary>
        [JsonPropertyName("metadata")]
        public YouApiMetadata? Metadata { get; set; }
    }
}
