namespace Mux.Server.Models
{
    /// <summary>
    /// A web search provider. The API key is never returned; <see cref="ApiKeySet"/> reports whether one is stored,
    /// and a blank <see cref="ApiKey"/> on save keeps the stored key.
    /// </summary>
    public class SearchProviderDto
    {
        /// <summary>A unique provider name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The provider type: <c>tavily</c> or <c>you</c>.</summary>
        public string ProviderType { get; set; } = "tavily";

        /// <summary>An optional API base URL override.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Whether an API key is stored (read only).</summary>
        public bool ApiKeySet { get; set; }

        /// <summary>A new API key (write only). Blank keeps the stored key.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Whether the provider is enabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Whether this is the default provider (the first enabled provider when none is marked).</summary>
        public bool IsDefault { get; set; }

        /// <summary>Request timeout in milliseconds.</summary>
        public int TimeoutMs { get; set; }
    }
}
