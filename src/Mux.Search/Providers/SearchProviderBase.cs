namespace Mux.Search.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Abstractions;
    using Mux.Search.Exceptions;
    using Mux.Search.Models;

    /// <summary>
    /// Shared HTTP plumbing for search providers.
    /// </summary>
    /// <typeparam name="TQuery">The provider query type.</typeparam>
    /// <typeparam name="TResponse">The provider response type.</typeparam>
    public abstract class SearchProviderBase<TQuery, TResponse> :
        ISearchProvider<TQuery, TResponse>,
        IDisposable
        where TQuery : SearchQuery
        where TResponse : SearchResponse
    {
        private readonly bool _OwnsHttpClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchProviderBase{TQuery, TResponse}"/> class.
        /// </summary>
        /// <param name="options">Provider options.</param>
        /// <param name="httpClient">Optional externally-owned HTTP client.</param>
        protected SearchProviderBase(SearchProviderOptions options, HttpClient? httpClient = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));

            if (string.IsNullOrWhiteSpace(options.Endpoint))
            {
                throw new ArgumentException("A provider endpoint is required.", nameof(options));
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey))
            {
                throw new ArgumentException("A provider API key is required.", nameof(options));
            }

            HttpClient = httpClient ?? new HttpClient();
            HttpClient.Timeout = options.Timeout;
            _OwnsHttpClient = httpClient is null;
        }

        /// <summary>
        /// Provider options.
        /// </summary>
        protected SearchProviderOptions Options { get; }

        /// <summary>
        /// Shared HTTP client.
        /// </summary>
        protected HttpClient HttpClient { get; }

        /// <summary>
        /// Shared serializer settings.
        /// </summary>
        protected JsonSerializerOptions SerializerOptions { get; } = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <inheritdoc />
        public abstract string ProviderName { get; }

        /// <inheritdoc />
        public abstract Task<TResponse> SearchAsync(TQuery query, CancellationToken cancellationToken = default);

        /// <summary>
        /// Disposes provider-owned resources.
        /// </summary>
        public void Dispose()
        {
            if (_OwnsHttpClient)
            {
                HttpClient.Dispose();
            }
        }

        /// <summary>
        /// Creates JSON request content.
        /// </summary>
        /// <param name="payload">Payload object.</param>
        /// <returns>The JSON request content.</returns>
        protected StringContent CreateJsonContent(object payload)
        {
            string json = JsonSerializer.Serialize(payload, SerializerOptions);
            return new StringContent(json, Encoding.UTF8, "application/json");
        }

        /// <summary>
        /// Executes an HTTP request and parses the JSON response body.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The typed response body and raw body.</returns>
        protected async Task<SearchProviderResponse<TBody>> SendAsync<TBody>(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            foreach (KeyValuePair<string, string> header in Options.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            using HttpResponseMessage response =
                await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            string rawJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new SearchProviderException(
                    ProviderName,
                    response.StatusCode,
                    rawJson,
                    $"The {ProviderName} request failed with status code {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            try
            {
                TBody? body = JsonSerializer.Deserialize<TBody>(rawJson, SerializerOptions);
                if (body == null)
                {
                    throw new JsonException("The response body was empty.");
                }

                return new SearchProviderResponse<TBody>(body, rawJson);
            }
            catch (JsonException ex)
            {
                throw new SearchProviderException(
                    ProviderName,
                    response.StatusCode,
                    rawJson,
                    $"The {ProviderName} response body was not valid JSON.",
                    ex);
            }
        }

        /// <summary>
        /// Builds a request URI from an endpoint and query-string parameters.
        /// </summary>
        /// <param name="endpoint">Base endpoint URL.</param>
        /// <param name="queryParameters">Query-string parameters.</param>
        /// <returns>A request URI.</returns>
        protected static Uri BuildUri(string endpoint, IEnumerable<KeyValuePair<string, string?>> queryParameters)
        {
            UriBuilder builder = new UriBuilder(endpoint);
            List<string> queryParts = new List<string>();

            if (!string.IsNullOrWhiteSpace(builder.Query))
            {
                queryParts.Add(builder.Query.TrimStart('?'));
            }

            queryParts.AddRange(
                queryParameters
                    .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                    .Select(parameter =>
                        $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value!)}"));

            builder.Query = string.Join("&", queryParts.Where(part => !string.IsNullOrWhiteSpace(part)));
            return builder.Uri;
        }
    }
}
