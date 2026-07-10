namespace Mux.Search.Providers.You
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
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
            SearchProviderResponse<YouApiResponse> providerResponse =
                await SendAsync<YouApiResponse>(request, cancellationToken).ConfigureAwait(false);

            YouApiResponse body = providerResponse.Body;
            YouSearchResponse response = new YouSearchResponse
            {
                ProviderName = ProviderName,
                Query = query.Query,
                RawJson = providerResponse.RawJson
            };

            if (body.Results != null)
            {
                response.SetSection("web", ConvertSection(body.Results.Web, "web"));
                response.SetSection("news", ConvertSection(body.Results.News, "news"));
            }

            if (body.Metadata != null)
            {
                response.SearchUuid = body.Metadata.SearchUuid;
                response.RequestId = response.SearchUuid;
                response.LatencySeconds = body.Metadata.Latency;
            }

            return response;
        }

        private static List<SearchResultItem> ConvertSection(List<YouApiResult>? apiResults, string sectionName)
        {
            List<SearchResultItem> results = new List<SearchResultItem>();
            if (apiResults == null)
            {
                return results;
            }

            foreach (YouApiResult item in apiResults)
            {
                List<string> snippets = item.Snippets ?? new List<string>();
                string? description = item.Description;

                if (!string.IsNullOrWhiteSpace(description) && !snippets.Contains(description))
                {
                    snippets.Insert(0, description);
                }

                results.Add(new SearchResultItem
                {
                    Section = sectionName,
                    Title = item.Title ?? string.Empty,
                    Url = item.Url ?? string.Empty,
                    Description = description,
                    Snippets = snippets,
                    FaviconUrl = item.FaviconUrl,
                    ThumbnailUrl = item.ThumbnailUrl,
                    RawContent = item.Content
                        ?? item.Markdown
                        ?? item.Html,
                    PublishedAt = item.PageAge
                });
            }

            return results;
        }
    }
}
