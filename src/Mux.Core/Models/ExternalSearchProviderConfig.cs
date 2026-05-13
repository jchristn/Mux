namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Configures one external web-search provider instance for mux.
    /// </summary>
    public class ExternalSearchProviderConfig
    {
        private string _Name = string.Empty;
        private string _ProviderType = string.Empty;
        private string _Endpoint = string.Empty;
        private string _ApiKey = string.Empty;
        private bool _Enabled = true;
        private bool _IsDefault = false;
        private int _TimeoutMs = 60000;

        /// <summary>
        /// Friendly provider configuration name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value?.Trim() ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// Provider type identifier such as tavily or you.
        /// </summary>
        [JsonPropertyName("providerType")]
        public string ProviderType
        {
            get => _ProviderType;
            set => _ProviderType = value?.Trim() ?? throw new ArgumentNullException(nameof(ProviderType));
        }

        /// <summary>
        /// Provider endpoint URL.
        /// </summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint
        {
            get => _Endpoint;
            set => _Endpoint = value?.Trim() ?? throw new ArgumentNullException(nameof(Endpoint));
        }

        /// <summary>
        /// Provider API key or environment-variable reference.
        /// </summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey
        {
            get => _ApiKey;
            set => _ApiKey = value?.Trim() ?? throw new ArgumentNullException(nameof(ApiKey));
        }

        /// <summary>
        /// Whether this provider is enabled for selection.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether this is the default provider.
        /// </summary>
        [JsonPropertyName("isDefault")]
        public bool IsDefault
        {
            get => _IsDefault;
            set => _IsDefault = value;
        }

        /// <summary>
        /// Request timeout in milliseconds.
        /// </summary>
        [JsonPropertyName("timeoutMs")]
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = Math.Clamp(value, 1000, 300000);
        }
    }
}
