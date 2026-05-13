namespace Mux.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Global external web-search configuration for mux.
    /// </summary>
    public class ExternalSearchSettings
    {
        private bool _Enabled = false;
        private bool _AllowFallback = true;
        private List<ExternalSearchProviderConfig> _Providers = new List<ExternalSearchProviderConfig>();

        /// <summary>
        /// Whether external search is enabled for mux tool exposure.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether mux may fall back to another configured provider when the first fails.
        /// </summary>
        [JsonPropertyName("allowFallback")]
        public bool AllowFallback
        {
            get => _AllowFallback;
            set => _AllowFallback = value;
        }

        /// <summary>
        /// Configured search providers.
        /// </summary>
        [JsonPropertyName("providers")]
        public List<ExternalSearchProviderConfig> Providers
        {
            get => _Providers;
            set => _Providers = value ?? new List<ExternalSearchProviderConfig>();
        }
    }
}
