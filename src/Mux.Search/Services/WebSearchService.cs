namespace Mux.Search.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Exceptions;
    using Mux.Search.Models;
    using Mux.Search.Providers.Tavily;
    using Mux.Search.Providers.You;

    /// <summary>
    /// Normalized web-search service with provider selection and fallback support.
    /// </summary>
    public class WebSearchService : IWebSearchService
    {
        private readonly WebSearchServiceOptions _Options;
        private readonly Func<SearchProviderOptions, HttpClient>? _HttpClientFactory;
        private readonly List<IConfiguredSearchProvider> _Providers;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSearchService"/> class.
        /// </summary>
        /// <param name="options">The service options.</param>
        public WebSearchService(WebSearchServiceOptions options)
            : this(options, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WebSearchService"/> class.
        /// </summary>
        /// <param name="options">The service options.</param>
        /// <param name="httpClientFactory">Optional HTTP client factory used for provider requests.</param>
        public WebSearchService(WebSearchServiceOptions options, Func<SearchProviderOptions, HttpClient>? httpClientFactory)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _HttpClientFactory = httpClientFactory;
            _Providers = BuildProviders(options);
        }

        /// <inheritdoc />
        public async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
        {
            if (!_Options.Enabled)
            {
                throw new InvalidOperationException("External web search is disabled.");
            }

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            request.Validate();

            List<IConfiguredSearchProvider> candidates = SelectCandidates(request);
            if (candidates.Count < 1)
            {
                throw new InvalidOperationException("No enabled external search providers are configured.");
            }

            List<WebSearchProviderAttempt> attempts = new List<WebSearchProviderAttempt>();
            Exception? lastException = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                IConfiguredSearchProvider provider = candidates[i];

                try
                {
                    WebSearchResponse response = await provider.SearchAsync(request, cancellationToken).ConfigureAwait(false);
                    attempts.Add(new WebSearchProviderAttempt
                    {
                        ProviderName = provider.Name,
                        ProviderType = provider.ProviderType,
                        Success = true,
                        Message = "Success."
                    });

                    response.UsedFallback = i > 0;
                    response.ProvidersTried = attempts.Select(attempt => attempt.ProviderName).ToList();
                    response.Attempts = attempts;
                    return response;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    attempts.Add(new WebSearchProviderAttempt
                    {
                        ProviderName = provider.Name,
                        ProviderType = provider.ProviderType,
                        Success = false,
                        Message = ex.Message
                    });

                    if (!_Options.AllowFallback)
                    {
                        break;
                    }
                }
            }

            throw new WebSearchException(
                "External web search failed across all attempted providers.",
                attempts,
                lastException);
        }

        private List<IConfiguredSearchProvider> BuildProviders(WebSearchServiceOptions options)
        {
            List<IConfiguredSearchProvider> providers = new List<IConfiguredSearchProvider>();

            foreach (WebSearchProviderRegistration registration in options.Providers ?? new List<WebSearchProviderRegistration>())
            {
                if (registration == null
                    || string.IsNullOrWhiteSpace(registration.Name)
                    || string.IsNullOrWhiteSpace(registration.ProviderType)
                    || string.IsNullOrWhiteSpace(registration.Options.Endpoint)
                    || string.IsNullOrWhiteSpace(registration.Options.ApiKey))
                {
                    continue;
                }

                string normalizedType = NormalizeProviderType(registration.ProviderType);
                switch (normalizedType)
                {
                    case "tavily":
                        providers.Add(new TavilyConfiguredSearchProvider(registration, _HttpClientFactory));
                        break;
                    case "you":
                        providers.Add(new YouConfiguredSearchProvider(registration, _HttpClientFactory));
                        break;
                }
            }

            return providers;
        }

        private List<IConfiguredSearchProvider> SelectCandidates(WebSearchRequest request)
        {
            List<IConfiguredSearchProvider> enabledProviders = _Providers
                .Where(provider => provider.Enabled)
                .ToList();

            if (enabledProviders.Count < 1)
            {
                return enabledProviders;
            }

            IConfiguredSearchProvider primary;

            if (!string.IsNullOrWhiteSpace(request.PreferredProvider))
            {
                primary = enabledProviders.FirstOrDefault(provider =>
                    string.Equals(provider.Name, request.PreferredProvider, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(provider.ProviderType, NormalizeProviderType(request.PreferredProvider), StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException($"No enabled search provider named '{request.PreferredProvider}' is configured.");
            }
            else
            {
                primary = enabledProviders.FirstOrDefault(provider => provider.IsDefault)
                    ?? enabledProviders[0];
            }

            if (!_Options.AllowFallback)
            {
                return new List<IConfiguredSearchProvider> { primary };
            }

            List<IConfiguredSearchProvider> ordered = new List<IConfiguredSearchProvider> { primary };
            ordered.AddRange(enabledProviders.Where(provider => !ReferenceEquals(provider, primary)));
            return ordered;
        }

        private static string NormalizeProviderType(string value)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "you.com" => "you",
                "ydc" => "you",
                _ => normalized
            };
        }

        private interface IConfiguredSearchProvider
        {
            string Name { get; }

            string ProviderType { get; }

            bool Enabled { get; }

            bool IsDefault { get; }

            Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken);
        }

        private abstract class ConfiguredSearchProviderBase : IConfiguredSearchProvider
        {
            private readonly Func<SearchProviderOptions, HttpClient>? _ProviderHttpClientFactory;

            protected ConfiguredSearchProviderBase(
                WebSearchProviderRegistration registration,
                Func<SearchProviderOptions, HttpClient>? httpClientFactory)
            {
                Registration = registration ?? throw new ArgumentNullException(nameof(registration));
                _ProviderHttpClientFactory = httpClientFactory;
            }

            protected WebSearchProviderRegistration Registration { get; }

            public string Name => Registration.Name;

            public string ProviderType => NormalizeProviderType(Registration.ProviderType);

            public bool Enabled => Registration.Enabled;

            public bool IsDefault => Registration.IsDefault;

            protected SearchProviderOptions CreateProviderOptions()
            {
                return new SearchProviderOptions
                {
                    Endpoint = Registration.Options.Endpoint,
                    ApiKey = Registration.Options.ApiKey,
                    Timeout = Registration.Options.Timeout,
                    Headers = new Dictionary<string, string>(Registration.Options.Headers)
                };
            }

            protected HttpClient CreateHttpClient()
            {
                SearchProviderOptions options = CreateProviderOptions();
                if (_ProviderHttpClientFactory != null)
                {
                    HttpClient configuredClient = _ProviderHttpClientFactory(options)
                        ?? throw new InvalidOperationException("The HTTP client factory returned null.");
                    configuredClient.Timeout = options.Timeout;
                    return configuredClient;
                }

                return new HttpClient
                {
                    Timeout = options.Timeout
                };
            }

            public abstract Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken);
        }

        private sealed class TavilyConfiguredSearchProvider : ConfiguredSearchProviderBase
        {
            public TavilyConfiguredSearchProvider(
                WebSearchProviderRegistration registration,
                Func<SearchProviderOptions, HttpClient>? httpClientFactory)
                : base(registration, httpClientFactory)
            {
            }

            public override async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken)
            {
                TavilySearchQuery query = new TavilySearchQuery
                {
                    Query = request.Query,
                    MaxResults = request.MaxResults,
                    IncludeDomains = new List<string>(request.IncludeDomains),
                    ExcludeDomains = new List<string>(request.ExcludeDomains),
                    IncludeAnswerMode = request.IncludeAnswer ? "basic" : null,
                    IncludeImages = request.IncludeImages,
                    IncludeImageDescriptions = request.IncludeImages,
                    TimeRange = request.Freshness
                };

                using HttpClient httpClient = CreateHttpClient();
                using TavilySearchClient client = new TavilySearchClient(CreateProviderOptions(), httpClient);
                TavilySearchResponse response = await client.SearchAsync(query, cancellationToken).ConfigureAwait(false);

                return new WebSearchResponse
                {
                    ProviderName = Name,
                    ProviderType = ProviderType,
                    Query = response.Query,
                    Answer = response.Answer,
                    RequestId = response.RequestId,
                    LatencySeconds = response.LatencySeconds,
                    Images = response.Images,
                    Sections = response.Sections,
                    RawJson = response.RawJson
                };
            }
        }

        private sealed class YouConfiguredSearchProvider : ConfiguredSearchProviderBase
        {
            public YouConfiguredSearchProvider(
                WebSearchProviderRegistration registration,
                Func<SearchProviderOptions, HttpClient>? httpClientFactory)
                : base(registration, httpClientFactory)
            {
            }

            public override async Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken)
            {
                YouSearchQuery query = new YouSearchQuery
                {
                    Query = request.Query,
                    MaxResults = request.MaxResults,
                    Offset = request.Offset,
                    IncludeDomains = new List<string>(request.IncludeDomains),
                    ExcludeDomains = new List<string>(request.ExcludeDomains),
                    Freshness = request.Freshness,
                    Livecrawl = null,
                    SafeSearch = "moderate"
                };

                using HttpClient httpClient = CreateHttpClient();
                using YouSearchClient client = new YouSearchClient(CreateProviderOptions(), httpClient);
                YouSearchResponse response = await client.SearchAsync(query, cancellationToken).ConfigureAwait(false);

                return new WebSearchResponse
                {
                    ProviderName = Name,
                    ProviderType = ProviderType,
                    Query = response.Query,
                    Answer = response.Answer,
                    RequestId = response.RequestId,
                    LatencySeconds = response.LatencySeconds,
                    Images = response.Images,
                    Sections = response.Sections,
                    RawJson = response.RawJson
                };
            }
        }
    }
}
