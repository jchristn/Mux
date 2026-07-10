namespace Mux.Search.Providers.Tavily
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed Tavily API result item.
    /// </summary>
    public class TavilyApiResult
    {
        /// <summary>
        /// Gets or sets the result title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the result URL.
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; }

        /// <summary>
        /// Gets or sets the result content.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// Gets or sets the provider score.
        /// </summary>
        [JsonPropertyName("score")]
        public double? Score { get; set; }

        /// <summary>
        /// Gets or sets raw content.
        /// </summary>
        [JsonPropertyName("raw_content")]
        public string? RawContent { get; set; }

        /// <summary>
        /// Gets or sets the favicon URL.
        /// </summary>
        [JsonPropertyName("favicon")]
        public string? Favicon { get; set; }

        /// <summary>
        /// Gets or sets the published date.
        /// </summary>
        [JsonPropertyName("published_date")]
        public DateTimeOffset? PublishedDate { get; set; }

        /// <summary>
        /// Gets or sets result-specific images.
        /// </summary>
        [JsonPropertyName("images")]
        [JsonConverter(typeof(TavilyApiImageListConverter))]
        public List<TavilyApiImage>? Images { get; set; }
    }
}
