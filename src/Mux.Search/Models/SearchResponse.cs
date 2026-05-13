namespace Mux.Search.Models
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Base class for provider-specific search responses.
    /// </summary>
    public abstract class SearchResponse
    {
        private Dictionary<string, List<SearchResultItem>> _Sections =
            new Dictionary<string, List<SearchResultItem>>(System.StringComparer.OrdinalIgnoreCase);

        private List<SearchImage> _Images = new List<SearchImage>();

        /// <summary>
        /// The provider name.
        /// </summary>
        public string ProviderName { get; set; } = string.Empty;

        /// <summary>
        /// The query echoed from the provider or request.
        /// </summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Optional provider-generated answer or summary.
        /// </summary>
        public string? Answer { get; set; }

        /// <summary>
        /// Optional provider request identifier.
        /// </summary>
        public string? RequestId { get; set; }

        /// <summary>
        /// Optional provider latency in seconds.
        /// </summary>
        public double? LatencySeconds { get; set; }

        /// <summary>
        /// Top-level images associated with the response.
        /// </summary>
        public List<SearchImage> Images
        {
            get => _Images;
            set => _Images = value ?? new List<SearchImage>();
        }

        /// <summary>
        /// Results grouped into provider sections such as web or news.
        /// </summary>
        public Dictionary<string, List<SearchResultItem>> Sections
        {
            get => _Sections;
            set => _Sections = value is null
                ? new Dictionary<string, List<SearchResultItem>>(System.StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, List<SearchResultItem>>(value, System.StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The raw provider JSON, retained for debugging.
        /// </summary>
        [JsonIgnore]
        public string? RawJson { get; set; }

        /// <summary>
        /// Flattened result list across sections.
        /// </summary>
        [JsonIgnore]
        public IReadOnlyList<SearchResultItem> Results => Sections.Values.SelectMany(values => values).ToList();

        /// <summary>
        /// Replaces a response section with the supplied results.
        /// </summary>
        /// <param name="sectionName">The section name.</param>
        /// <param name="results">The result items.</param>
        public void SetSection(string sectionName, IEnumerable<SearchResultItem>? results)
        {
            Sections[sectionName] = results?.ToList() ?? new List<SearchResultItem>();
        }
    }
}
