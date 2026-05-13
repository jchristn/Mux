namespace Mux.Search.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Represents a normalized search result entry.
    /// </summary>
    public class SearchResultItem
    {
        private List<string> _Snippets = new List<string>();
        private List<SearchImage> _Images = new List<SearchImage>();

        /// <summary>
        /// The response section containing the result.
        /// </summary>
        public string Section { get; set; } = "web";

        /// <summary>
        /// The result title.
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// The canonical result URL.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Optional human-readable summary.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional provider snippets.
        /// </summary>
        public List<string> Snippets
        {
            get => _Snippets;
            set => _Snippets = value ?? new List<string>();
        }

        /// <summary>
        /// Optional provider score.
        /// </summary>
        public double? Score { get; set; }

        /// <summary>
        /// Optional raw provider content.
        /// </summary>
        public string? RawContent { get; set; }

        /// <summary>
        /// Optional favicon URL.
        /// </summary>
        public string? FaviconUrl { get; set; }

        /// <summary>
        /// Optional thumbnail URL.
        /// </summary>
        public string? ThumbnailUrl { get; set; }

        /// <summary>
        /// Optional published timestamp.
        /// </summary>
        public DateTimeOffset? PublishedAt { get; set; }

        /// <summary>
        /// Optional result-specific images.
        /// </summary>
        public List<SearchImage> Images
        {
            get => _Images;
            set => _Images = value ?? new List<SearchImage>();
        }
    }
}
