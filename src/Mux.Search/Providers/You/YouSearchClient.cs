namespace Mux.Search.Providers.You
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Internal;
    using Mux.Search.Models;

    /// <summary>
    /// Search client for the You.com search API.
    /// </summary>
    public class YouSearchClient : SearchProviderBase<YouSearchQuery, YouSearchResponse>
    {
        /// <summary>
        /// The default You.com search endpoint.
        /// </summary>
        public const string DefaultEndpoint = "https://ydc-index.io/v1/search";

        /// <summary>
        /// Initializes a new instance of the <see cref="YouSearchClient"/> class.
        /// </summary>
        /// <param name="options">Provider options.</param>
        /// <param name="httpClient">Optional externally-owned HTTP client.</param>
        public YouSearchClient(SearchProviderOptions options, HttpClient? httpClient = null)
            : base(options, httpClient)
        {
        }

        /// <inheritdoc />
        public override string ProviderName => "You.com";

        /// <inheritdoc />
        public override async Task<YouSearchResponse> SearchAsync(
            YouSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            query.Validate();

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Options.Endpoint);
            request.Headers.Add("X-API-Key", Options.ApiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = CreateJsonContent(new
            {
                query = query.Query,
                count = query.MaxResults,
                offset = query.Offset,
                country = query.Country,
                language = query.Language,
                safesearch = query.SafeSearch,
                freshness = query.Freshness,
                livecrawl = query.Livecrawl,
                livecrawl_formats = query.LivecrawlFormats.Count > 0 ? query.LivecrawlFormats : null,
                include_domains = query.IncludeDomains.Count > 0 ? query.IncludeDomains : null,
                exclude_domains = query.ExcludeDomains.Count > 0 ? query.ExcludeDomains : null,
                boost_domains = query.BoostDomains.Count > 0 ? query.BoostDomains : null,
                crawl_timeout = query.CrawlTimeoutSeconds
            });
            (JsonDocument document, string rawJson) =
                await SendAsync(request, cancellationToken).ConfigureAwait(false);

            using (document)
            {
                JsonElement root = document.RootElement;
                YouSearchResponse response = new YouSearchResponse
                {
                    ProviderName = ProviderName,
                    Query = query.Query,
                    RawJson = rawJson
                };

                JsonElement? resultsRoot = root.GetPropertyOrNull("results");
                if (resultsRoot.HasValue && resultsRoot.Value.ValueKind == JsonValueKind.Object)
                {
                    response.SetSection("web", ParseSection(resultsRoot.Value.GetPropertyOrNull("web"), "web"));
                    response.SetSection("news", ParseSection(resultsRoot.Value.GetPropertyOrNull("news"), "news"));
                }

                JsonElement? metadata = root.GetPropertyOrNull("metadata");
                if (metadata.HasValue && metadata.Value.ValueKind == JsonValueKind.Object)
                {
                    response.SearchUuid = metadata.Value.GetStringOrNull("search_uuid");
                    response.RequestId = response.SearchUuid;
                    response.LatencySeconds = metadata.Value.GetDoubleOrNull("latency");
                }

                return response;
            }
        }

        private static List<SearchResultItem> ParseSection(JsonElement? resultsElement, string sectionName)
        {
            List<SearchResultItem> results = new List<SearchResultItem>();
            if (!resultsElement.HasValue || resultsElement.Value.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (JsonElement item in resultsElement.Value.EnumerateArray())
            {
                List<string> snippets = item.GetStringListOrEmpty("snippets");
                string? description = item.GetStringOrNull("description");

                if (!string.IsNullOrWhiteSpace(description) && !snippets.Contains(description))
                {
                    snippets.Insert(0, description);
                }

                results.Add(new SearchResultItem
                {
                    Section = sectionName,
                    Title = item.GetStringOrNull("title") ?? string.Empty,
                    Url = item.GetStringOrNull("url") ?? string.Empty,
                    Description = description,
                    Snippets = snippets,
                    FaviconUrl = item.GetStringOrNull("favicon_url"),
                    ThumbnailUrl = item.GetStringOrNull("thumbnail_url"),
                    RawContent = item.GetStringOrNull("content")
                        ?? item.GetStringOrNull("markdown")
                        ?? item.GetStringOrNull("html"),
                    PublishedAt = item.GetDateTimeOffsetOrNull("page_age")
                });
            }

            return results;
        }
    }
}
