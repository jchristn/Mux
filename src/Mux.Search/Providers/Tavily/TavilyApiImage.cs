namespace Mux.Search.Providers.Tavily
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed Tavily API image.
    /// </summary>
    public class TavilyApiImage
    {
        /// <summary>
        /// Gets or sets the image URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// Gets or sets the alternate image URL.
        /// </summary>
        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; set; }

        /// <summary>
        /// Gets or sets the image description.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the image alt text.
        /// </summary>
        [JsonPropertyName("alt")]
        public string? Alt { get; set; }
    }
}
