namespace Mux.Search.Providers.You
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed You.com API result item.
    /// </summary>
    public class YouApiResult
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
        /// Gets or sets the description.
        /// </summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets snippets.
        /// </summary>
        [JsonPropertyName("snippets")]
        public List<string>? Snippets { get; set; }

        /// <summary>
        /// Gets or sets the favicon URL.
        /// </summary>
        [JsonPropertyName("favicon_url")]
        public string? FaviconUrl { get; set; }

        /// <summary>
        /// Gets or sets the thumbnail URL.
        /// </summary>
        [JsonPropertyName("thumbnail_url")]
        public string? ThumbnailUrl { get; set; }

        /// <summary>
        /// Gets or sets raw content.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        /// <summary>
        /// Gets or sets markdown content.
        /// </summary>
        [JsonPropertyName("markdown")]
        public string? Markdown { get; set; }

        /// <summary>
        /// Gets or sets HTML content.
        /// </summary>
        [JsonPropertyName("html")]
        public string? Html { get; set; }

        /// <summary>
        /// Gets or sets page age.
        /// </summary>
        [JsonPropertyName("page_age")]
        public DateTimeOffset? PageAge { get; set; }
    }
}
