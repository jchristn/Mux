namespace Mux.Search.Providers.Tavily
{
    using Mux.Search.Models;

    /// <summary>
    /// Response model for Tavily search requests.
    /// </summary>
    public class TavilySearchResponse : SearchResponse
    {
        /// <summary>
        /// Optional provider-selected parameters.
        /// </summary>
        public TavilyAutoParameters? AutoParameters { get; set; }

        /// <summary>
        /// Optional provider usage metadata.
        /// </summary>
        public TavilyUsage? Usage { get; set; }
    }

    /// <summary>
    /// Provider-selected parameters returned by Tavily.
    /// </summary>
    public class TavilyAutoParameters
    {
        /// <summary>
        /// Provider-selected topic.
        /// </summary>
        public string? Topic { get; set; }

        /// <summary>
        /// Provider-selected search depth.
        /// </summary>
        public string? SearchDepth { get; set; }
    }

    /// <summary>
    /// Tavily usage metadata.
    /// </summary>
    public class TavilyUsage
    {
        /// <summary>
        /// Credit usage for the request.
        /// </summary>
        public int? CreditsUsed { get; set; }
    }
}
