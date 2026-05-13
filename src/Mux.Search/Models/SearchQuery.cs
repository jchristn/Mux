namespace Mux.Search.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Base class for provider-specific search queries.
    /// </summary>
    public abstract class SearchQuery
    {
        private string _Query = string.Empty;
        private int _MaxResults = 5;
        private int _Offset = 0;
        private List<string> _IncludeDomains = new List<string>();
        private List<string> _ExcludeDomains = new List<string>();

        /// <summary>
        /// The user query text.
        /// </summary>
        public string Query
        {
            get => _Query;
            set => _Query = value?.Trim() ?? throw new ArgumentNullException(nameof(Query));
        }

        /// <summary>
        /// The desired maximum number of results.
        /// </summary>
        public int MaxResults
        {
            get => _MaxResults;
            set => _MaxResults = Math.Clamp(value, 1, 100);
        }

        /// <summary>
        /// Optional result offset for providers that support paging.
        /// </summary>
        public int Offset
        {
            get => _Offset;
            set => _Offset = Math.Max(0, value);
        }

        /// <summary>
        /// Domains to explicitly include.
        /// </summary>
        public List<string> IncludeDomains
        {
            get => _IncludeDomains;
            set => _IncludeDomains = NormalizeValues(value);
        }

        /// <summary>
        /// Domains to explicitly exclude.
        /// </summary>
        public List<string> ExcludeDomains
        {
            get => _ExcludeDomains;
            set => _ExcludeDomains = NormalizeValues(value);
        }

        /// <summary>
        /// Validates the query instance.
        /// </summary>
        public virtual void Validate()
        {
            if (string.IsNullOrWhiteSpace(Query))
            {
                throw new ArgumentException("A search query is required.", nameof(Query));
            }
        }

        /// <summary>
        /// Normalizes a set of string values for provider requests.
        /// </summary>
        /// <param name="values">Input values.</param>
        /// <returns>A normalized list.</returns>
        protected static List<string> NormalizeValues(IEnumerable<string>? values)
        {
            return values is null
                ? new List<string>()
                : values
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }
    }
}
