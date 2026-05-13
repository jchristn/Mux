namespace Mux.Search.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Provider-agnostic request model for web search.
    /// </summary>
    public class WebSearchRequest
    {
        private string _Query = string.Empty;
        private int _MaxResults = 5;
        private int _Offset = 0;
        private List<string> _IncludeDomains = new List<string>();
        private List<string> _ExcludeDomains = new List<string>();

        /// <summary>
        /// Search query text.
        /// </summary>
        public string Query
        {
            get => _Query;
            set => _Query = value?.Trim() ?? throw new ArgumentNullException(nameof(Query));
        }

        /// <summary>
        /// Desired maximum number of results.
        /// </summary>
        public int MaxResults
        {
            get => _MaxResults;
            set => _MaxResults = Math.Clamp(value, 1, 20);
        }

        /// <summary>
        /// Optional result offset where supported.
        /// </summary>
        public int Offset
        {
            get => _Offset;
            set => _Offset = Math.Clamp(value, 0, 9);
        }

        /// <summary>
        /// Optional provider name or type to prefer for this request.
        /// </summary>
        public string? PreferredProvider { get; set; }

        /// <summary>
        /// Optional freshness filter.
        /// </summary>
        public string? Freshness { get; set; }

        /// <summary>
        /// Whether to request provider-generated summaries when supported.
        /// </summary>
        public bool IncludeAnswer { get; set; } = true;

        /// <summary>
        /// Whether to request images when supported.
        /// </summary>
        public bool IncludeImages { get; set; }

        /// <summary>
        /// Domains to include.
        /// </summary>
        public List<string> IncludeDomains
        {
            get => _IncludeDomains;
            set => _IncludeDomains = SearchQuery.NormalizeValues(value);
        }

        /// <summary>
        /// Domains to exclude.
        /// </summary>
        public List<string> ExcludeDomains
        {
            get => _ExcludeDomains;
            set => _ExcludeDomains = SearchQuery.NormalizeValues(value);
        }

        /// <summary>
        /// Validates the request.
        /// </summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Query))
            {
                throw new ArgumentException("A web-search query is required.", nameof(Query));
            }
        }
    }
}
