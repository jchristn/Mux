namespace Mux.Search.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Shared HTTP and authentication settings for a search provider.
    /// </summary>
    public class SearchProviderOptions
    {
        private string _Endpoint = string.Empty;
        private string _ApiKey = string.Empty;
        private TimeSpan _Timeout = TimeSpan.FromSeconds(60);
        private Dictionary<string, string> _Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The provider endpoint URL.
        /// </summary>
        public string Endpoint
        {
            get => _Endpoint;
            set => _Endpoint = value?.Trim() ?? throw new ArgumentNullException(nameof(Endpoint));
        }

        /// <summary>
        /// The provider API key.
        /// </summary>
        public string ApiKey
        {
            get => _ApiKey;
            set => _ApiKey = value?.Trim() ?? throw new ArgumentNullException(nameof(ApiKey));
        }

        /// <summary>
        /// Request timeout.
        /// </summary>
        public TimeSpan Timeout
        {
            get => _Timeout;
            set => _Timeout = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(60) : value;
        }

        /// <summary>
        /// Optional additional headers.
        /// </summary>
        public Dictionary<string, string> Headers
        {
            get => _Headers;
            set => _Headers = value is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(value, StringComparer.OrdinalIgnoreCase);
        }
    }
}
