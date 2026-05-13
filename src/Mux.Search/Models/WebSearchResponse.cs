namespace Mux.Search.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Provider-agnostic response model for web search.
    /// </summary>
    public class WebSearchResponse : SearchResponse
    {
        private List<string> _ProvidersTried = new List<string>();
        private List<WebSearchProviderAttempt> _Attempts = new List<WebSearchProviderAttempt>();

        /// <summary>
        /// Provider type used for the successful result.
        /// </summary>
        public string ProviderType { get; set; } = string.Empty;

        /// <summary>
        /// Whether the result came from a fallback provider.
        /// </summary>
        public bool UsedFallback { get; set; }

        /// <summary>
        /// Ordered list of provider names tried for the request.
        /// </summary>
        public List<string> ProvidersTried
        {
            get => _ProvidersTried;
            set => _ProvidersTried = value ?? new List<string>();
        }

        /// <summary>
        /// Provider attempt details.
        /// </summary>
        public List<WebSearchProviderAttempt> Attempts
        {
            get => _Attempts;
            set => _Attempts = value ?? new List<WebSearchProviderAttempt>();
        }
    }
}
