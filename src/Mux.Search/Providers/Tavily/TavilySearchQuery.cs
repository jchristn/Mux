namespace Mux.Search.Providers.Tavily
{
    using System;
    using Mux.Search.Models;

    /// <summary>
    /// Query model for Tavily search requests.
    /// </summary>
    public class TavilySearchQuery : SearchQuery
    {
        private string _SearchDepth = "basic";
        private string _Topic = "general";
        private int _ChunksPerSource = 3;

        /// <summary>
        /// Tavily search depth, such as basic or advanced.
        /// </summary>
        public string SearchDepth
        {
            get => _SearchDepth;
            set => _SearchDepth = string.IsNullOrWhiteSpace(value) ? "basic" : value.Trim();
        }

        /// <summary>
        /// Tavily topic, such as general or news.
        /// </summary>
        public string Topic
        {
            get => _Topic;
            set => _Topic = string.IsNullOrWhiteSpace(value) ? "general" : value.Trim();
        }

        /// <summary>
        /// Provider chunk count per source.
        /// </summary>
        public int ChunksPerSource
        {
            get => _ChunksPerSource;
            set => _ChunksPerSource = Math.Clamp(value, 1, 3);
        }

        /// <summary>
        /// Optional relative time range.
        /// </summary>
        public string? TimeRange { get; set; }

        /// <summary>
        /// Optional absolute start date.
        /// </summary>
        public DateOnly? StartDate { get; set; }

        /// <summary>
        /// Optional absolute end date.
        /// </summary>
        public DateOnly? EndDate { get; set; }

        /// <summary>
        /// Optional include-answer mode.
        /// </summary>
        public string? IncludeAnswerMode { get; set; } = "basic";

        /// <summary>
        /// Optional include-raw-content mode.
        /// </summary>
        public string? IncludeRawContentMode { get; set; }

        /// <summary>
        /// Whether to request images.
        /// </summary>
        public bool IncludeImages { get; set; }

        /// <summary>
        /// Whether to request image descriptions.
        /// </summary>
        public bool IncludeImageDescriptions { get; set; }

        /// <summary>
        /// Whether to request favicons.
        /// </summary>
        public bool IncludeFavicon { get; set; } = true;

        /// <summary>
        /// Optional country hint.
        /// </summary>
        public string? Country { get; set; }

        /// <summary>
        /// Whether to allow Tavily auto-parameter selection.
        /// </summary>
        public bool AutoParameters { get; set; }

        /// <summary>
        /// Whether to require exact matching semantics where supported.
        /// </summary>
        public bool ExactMatch { get; set; }

        /// <summary>
        /// Whether to request usage data.
        /// </summary>
        public bool IncludeUsage { get; set; } = true;

        /// <summary>
        /// Whether to request safe-search filtering.
        /// </summary>
        public bool SafeSearch { get; set; }

        /// <inheritdoc />
        public override void Validate()
        {
            base.Validate();

            if (MaxResults > 20)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxResults), "Tavily supports at most 20 results per request.");
            }

            if (StartDate.HasValue && EndDate.HasValue && EndDate.Value < StartDate.Value)
            {
                throw new ArgumentException("EndDate must be greater than or equal to StartDate.");
            }
        }
    }
}
