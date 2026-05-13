namespace Mux.Search.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// Top-level options for the normalized web-search service.
    /// </summary>
    public class WebSearchServiceOptions
    {
        private bool _Enabled = true;
        private bool _AllowFallback = true;
        private List<WebSearchProviderRegistration> _Providers = new List<WebSearchProviderRegistration>();

        /// <summary>
        /// Whether the service is enabled.
        /// </summary>
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Whether provider fallback is allowed.
        /// </summary>
        public bool AllowFallback
        {
            get => _AllowFallback;
            set => _AllowFallback = value;
        }

        /// <summary>
        /// Configured provider registrations.
        /// </summary>
        public List<WebSearchProviderRegistration> Providers
        {
            get => _Providers;
            set => _Providers = value ?? new List<WebSearchProviderRegistration>();
        }
    }
}
