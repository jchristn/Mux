namespace Mux.Core.Search
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Search.Models;
    using Mux.Search.Services;

    /// <summary>
    /// Builds mux-facing web-search services from persisted mux settings.
    /// </summary>
    public static class WebSearchServiceFactory
    {
        /// <summary>
        /// Creates a configured web-search service when external search is enabled and usable.
        /// </summary>
        /// <param name="settings">The mux settings to bind.</param>
        /// <returns>A configured service, or null when search is disabled or unconfigured.</returns>
        public static IWebSearchService? Create(MuxSettings? settings)
        {
            if (settings == null || !settings.ExternalSearch.Enabled)
            {
                return null;
            }

            WebSearchServiceOptions options = CreateOptions(settings);
            if (options.Providers.Count < 1)
            {
                return null;
            }

            return new WebSearchService(options);
        }

        /// <summary>
        /// Binds mux search settings to the provider-agnostic search service options.
        /// </summary>
        /// <param name="settings">The mux settings to bind.</param>
        /// <returns>The normalized service options.</returns>
        public static WebSearchServiceOptions CreateOptions(MuxSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            List<WebSearchProviderRegistration> providers = new List<WebSearchProviderRegistration>();

            foreach (ExternalSearchProviderConfig provider in settings.ExternalSearch.Providers ?? new List<ExternalSearchProviderConfig>())
            {
                if (provider == null
                    || string.IsNullOrWhiteSpace(provider.Name)
                    || string.IsNullOrWhiteSpace(provider.ProviderType)
                    || string.IsNullOrWhiteSpace(provider.Endpoint)
                    || string.IsNullOrWhiteSpace(provider.ApiKey))
                {
                    continue;
                }

                providers.Add(new WebSearchProviderRegistration
                {
                    Name = provider.Name,
                    ProviderType = provider.ProviderType,
                    Enabled = provider.Enabled,
                    IsDefault = provider.IsDefault,
                    Options = new SearchProviderOptions
                    {
                        Endpoint = SettingsLoader.ExpandEnvironmentVariables(provider.Endpoint),
                        ApiKey = SettingsLoader.ExpandEnvironmentVariables(provider.ApiKey),
                        Timeout = TimeSpan.FromMilliseconds(provider.TimeoutMs)
                    }
                });
            }

            return new WebSearchServiceOptions
            {
                Enabled = settings.ExternalSearch.Enabled,
                AllowFallback = settings.ExternalSearch.AllowFallback,
                Providers = NormalizeProviders(providers)
            };
        }

        private static List<WebSearchProviderRegistration> NormalizeProviders(List<WebSearchProviderRegistration> providers)
        {
            List<WebSearchProviderRegistration> normalized = providers
                .Where(provider => provider != null)
                .Select(CloneProvider)
                .ToList();

            bool defaultAssigned = false;
            foreach (WebSearchProviderRegistration provider in normalized)
            {
                if (provider.Enabled && provider.IsDefault && !defaultAssigned)
                {
                    defaultAssigned = true;
                }
                else
                {
                    provider.IsDefault = false;
                }
            }

            if (!defaultAssigned)
            {
                WebSearchProviderRegistration? firstEnabled = normalized.FirstOrDefault(provider => provider.Enabled);
                if (firstEnabled != null)
                {
                    firstEnabled.IsDefault = true;
                }
            }

            return normalized;
        }

        private static WebSearchProviderRegistration CloneProvider(WebSearchProviderRegistration source)
        {
            return new WebSearchProviderRegistration
            {
                Name = source.Name,
                ProviderType = source.ProviderType,
                Enabled = source.Enabled,
                IsDefault = source.IsDefault,
                Options = new SearchProviderOptions
                {
                    Endpoint = source.Options.Endpoint,
                    ApiKey = source.Options.ApiKey,
                    Timeout = source.Options.Timeout,
                    Headers = new Dictionary<string, string>(source.Options.Headers)
                }
            };
        }
    }
}
