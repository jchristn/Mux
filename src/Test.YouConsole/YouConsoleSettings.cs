namespace Test.YouConsole
{
    using System;
    using System.Collections.Generic;
    using Mux.Search.Models;
    using Mux.Search.Providers.You;

    /// <summary>
    /// Mutable runtime settings for the You.com console harness.
    /// </summary>
    internal class YouConsoleSettings
    {
        public string Endpoint { get; set; } =
            Environment.GetEnvironmentVariable("YOU_SEARCH_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("YOU_ENDPOINT")
            ?? YouSearchClient.DefaultEndpoint;

        public string ApiKey { get; set; } =
            Environment.GetEnvironmentVariable("YOU_API_KEY")
            ?? Environment.GetEnvironmentVariable("YDC_API_KEY")
            ?? string.Empty;

        public int TimeoutSeconds { get; set; } = 60;

        public int MaxResults { get; set; } = 5;

        public int Offset { get; set; }

        public string Language { get; set; } = "en";

        public string SafeSearch { get; set; } = "moderate";

        public string? Country { get; set; }

        public string? Freshness { get; set; }

        public string? Livecrawl { get; set; }

        public int CrawlTimeoutSeconds { get; set; } = 15;

        public List<string> LivecrawlFormats { get; set; } = new List<string>();

        public List<string> IncludeDomains { get; set; } = new List<string>();

        public List<string> ExcludeDomains { get; set; } = new List<string>();

        public List<string> BoostDomains { get; set; } = new List<string>();

        public SearchProviderOptions ToProviderOptions()
        {
            return new SearchProviderOptions
            {
                Endpoint = Endpoint,
                ApiKey = ApiKey,
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
        }

        public YouSearchQuery ToQuery(string query)
        {
            return new YouSearchQuery
            {
                Query = query,
                MaxResults = MaxResults,
                Offset = Offset,
                Language = Language,
                SafeSearch = SafeSearch,
                Country = Country,
                Freshness = Freshness,
                Livecrawl = Livecrawl,
                CrawlTimeoutSeconds = CrawlTimeoutSeconds,
                LivecrawlFormats = new List<string>(LivecrawlFormats),
                IncludeDomains = new List<string>(IncludeDomains),
                ExcludeDomains = new List<string>(ExcludeDomains),
                BoostDomains = new List<string>(BoostDomains)
            };
        }
    }
}
