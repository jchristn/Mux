namespace Mux.Search.Providers.You
{
    using System;
    using System.Collections.Generic;
    using Mux.Search.Models;

    /// <summary>
    /// Query model for You.com search requests.
    /// </summary>
    public class YouSearchQuery : SearchQuery
    {
        private string _Language = "en";
        private string _SafeSearch = "moderate";
        private List<string> _LivecrawlFormats = new List<string>();
        private List<string> _BoostDomains = new List<string>();
        private int _CrawlTimeoutSeconds = 15;

        /// <summary>
        /// Optional freshness filter.
        /// </summary>
        public string? Freshness { get; set; }

        /// <summary>
        /// Optional country hint.
        /// </summary>
        public string? Country { get; set; }

        /// <summary>
        /// UI language hint.
        /// </summary>
        public string Language
        {
            get => _Language;
            set => _Language = string.IsNullOrWhiteSpace(value) ? "en" : value.Trim();
        }

        /// <summary>
        /// Safe-search setting.
        /// </summary>
        public string SafeSearch
        {
            get => _SafeSearch;
            set => _SafeSearch = string.IsNullOrWhiteSpace(value) ? "moderate" : value.Trim();
        }

        /// <summary>
        /// Livecrawl mode when provider crawling is requested.
        /// </summary>
        public string? Livecrawl { get; set; }

        /// <summary>
        /// Requested livecrawl output formats.
        /// </summary>
        public List<string> LivecrawlFormats
        {
            get => _LivecrawlFormats;
            set => _LivecrawlFormats = NormalizeValues(value);
        }

        /// <summary>
        /// Domains to boost.
        /// </summary>
        public List<string> BoostDomains
        {
            get => _BoostDomains;
            set => _BoostDomains = NormalizeValues(value);
        }

        /// <summary>
        /// Crawl timeout in seconds.
        /// </summary>
        public int CrawlTimeoutSeconds
        {
            get => _CrawlTimeoutSeconds;
            set => _CrawlTimeoutSeconds = Math.Clamp(value, 1, 60);
        }

        /// <inheritdoc />
        public override void Validate()
        {
            base.Validate();

            if (Offset > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(Offset), "You.com currently supports offsets from 0 to 9.");
            }

            if (IncludeDomains.Count > 0 && ExcludeDomains.Count > 0)
            {
                throw new ArgumentException("You.com requests should specify include_domains or exclude_domains, but not both.");
            }
        }
    }
}
