namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Web search settings: whether the model may search the web, whether it may fall back to the next provider when
    /// one fails, and the providers.
    /// </summary>
    public class SearchSettingsDto
    {
        /// <summary>Whether web search is enabled.</summary>
        public bool Enabled { get; set; }

        /// <summary>Whether a failing provider falls back to the next enabled one.</summary>
        public bool AllowFallback { get; set; } = true;

        /// <summary>The providers, in order.</summary>
        public List<SearchProviderDto> Providers { get; set; } = new List<SearchProviderDto>();

        /// <summary>The provider types this machine supports (read only).</summary>
        public List<string> ProviderTypes { get; set; } = new List<string>();
    }
}
