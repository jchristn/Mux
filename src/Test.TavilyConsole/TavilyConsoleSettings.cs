namespace Test.TavilyConsole
{
    using System;
    using System.Collections.Generic;
    using Mux.Search.Models;
    using Mux.Search.Providers.Tavily;

    /// <summary>
    /// Mutable runtime settings for the Tavily console harness.
    /// </summary>
    internal class TavilyConsoleSettings
    {
        public string Endpoint { get; set; } =
            Environment.GetEnvironmentVariable("TAVILY_SEARCH_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("TAVILY_ENDPOINT")
            ?? TavilySearchClient.DefaultEndpoint;

        public string ApiKey { get; set; } =
            Environment.GetEnvironmentVariable("TAVILY_API_KEY")
            ?? string.Empty;

        public int TimeoutSeconds { get; set; } = 60;

        public int MaxResults { get; set; } = 5;

        public string SearchDepth { get; set; } = "basic";

        public string Topic { get; set; } = "general";

        public string? TimeRange { get; set; }

        public string? IncludeAnswerMode { get; set; } = "basic";

        public string? IncludeRawContentMode { get; set; }

        public bool IncludeImages { get; set; }

        public bool IncludeImageDescriptions { get; set; }

        public bool IncludeFavicon { get; set; } = true;

        public bool AutoParameters { get; set; }

        public bool ExactMatch { get; set; }

        public bool IncludeUsage { get; set; } = true;

        public bool SafeSearch { get; set; }

        public List<string> IncludeDomains { get; set; } = new List<string>();

        public List<string> ExcludeDomains { get; set; } = new List<string>();

        public SearchProviderOptions ToProviderOptions()
        {
            return new SearchProviderOptions
            {
                Endpoint = Endpoint,
                ApiKey = ApiKey,
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
        }

        public TavilySearchQuery ToQuery(string query)
        {
            return new TavilySearchQuery
            {
                Query = query,
                MaxResults = MaxResults,
                SearchDepth = SearchDepth,
                Topic = Topic,
                TimeRange = TimeRange,
                IncludeAnswerMode = IncludeAnswerMode,
                IncludeRawContentMode = IncludeRawContentMode,
                IncludeImages = IncludeImages,
                IncludeImageDescriptions = IncludeImageDescriptions,
                IncludeFavicon = IncludeFavicon,
                AutoParameters = AutoParameters,
                ExactMatch = ExactMatch,
                IncludeUsage = IncludeUsage,
                SafeSearch = SafeSearch,
                IncludeDomains = new List<string>(IncludeDomains),
                ExcludeDomains = new List<string>(ExcludeDomains)
            };
        }
    }
}
