namespace Mux.Search.Providers.Tavily
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Models;

    /// <summary>
    /// Search client for the Tavily search API.
    /// </summary>
    public class TavilySearchClient : SearchProviderBase<TavilySearchQuery, TavilySearchResponse>
    {
        /// <summary>
        /// The default Tavily search endpoint.
        /// </summary>
        public const string DefaultEndpoint = "https://api.tavily.com/search";

        /// <summary>
        /// Initializes a new instance of the <see cref="TavilySearchClient"/> class.
        /// </summary>
        /// <param name="options">Provider options.</param>
        /// <param name="httpClient">Optional externally-owned HTTP client.</param>
        public TavilySearchClient(SearchProviderOptions options, HttpClient? httpClient = null)
            : base(options, httpClient)
        {
        }

        /// <inheritdoc />
        public override string ProviderName => "Tavily";

        /// <inheritdoc />
        public override async Task<TavilySearchResponse> SearchAsync(
            TavilySearchQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query is null)
            {
                throw new ArgumentNullException(nameof(query));
            }

            query.Validate();

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, Options.Endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Options.ApiKey);
            request.Content = CreateJsonContent(new
            {
                query = query.Query,
                search_depth = query.SearchDepth,
                topic = query.Topic,
                max_results = query.MaxResults,
                chunks_per_source = query.ChunksPerSource,
                time_range = query.TimeRange,
                start_date = query.StartDate?.ToString("yyyy-MM-dd"),
                end_date = query.EndDate?.ToString("yyyy-MM-dd"),
                include_answer = NormalizeOptionalMode(query.IncludeAnswerMode),
                include_raw_content = NormalizeOptionalMode(query.IncludeRawContentMode),
                include_images = query.IncludeImages,
                include_image_descriptions = query.IncludeImageDescriptions,
                include_favicon = query.IncludeFavicon,
                include_domains = query.IncludeDomains.Count > 0 ? query.IncludeDomains : null,
                exclude_domains = query.ExcludeDomains.Count > 0 ? query.ExcludeDomains : null,
                country = query.Country,
                auto_parameters = query.AutoParameters,
                exact_match = query.ExactMatch,
                include_usage = query.IncludeUsage,
                safe_search = query.SafeSearch
            });

            SearchProviderResponse<TavilyApiResponse> providerResponse =
                await SendAsync<TavilyApiResponse>(request, cancellationToken).ConfigureAwait(false);

            TavilyApiResponse body = providerResponse.Body;
            TavilySearchResponse response = new TavilySearchResponse
            {
                ProviderName = ProviderName,
                Query = body.Query ?? query.Query,
                Answer = body.Answer,
                RequestId = body.RequestId,
                LatencySeconds = body.ResponseTime,
                Images = ConvertImages(body.Images),
                RawJson = providerResponse.RawJson
            };

            response.SetSection("web", ConvertResults(body.Results));

            if (body.AutoParameters != null)
            {
                response.AutoParameters = new TavilyAutoParameters
                {
                    Topic = body.AutoParameters.Topic,
                    SearchDepth = body.AutoParameters.SearchDepth
                };
            }

            if (body.Usage != null)
            {
                response.Usage = new TavilyUsage
                {
                    CreditsUsed = body.Usage.CreditsUsed ?? body.Usage.Credits
                };
            }

            return response;
        }

        private static object NormalizeOptionalMode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (bool.TryParse(value, out bool boolValue))
            {
                return boolValue;
            }

            return value.Trim();
        }

        private static List<SearchResultItem> ConvertResults(List<TavilyApiResult>? apiResults)
        {
            List<SearchResultItem> results = new List<SearchResultItem>();
            if (apiResults == null)
            {
                return results;
            }

            foreach (TavilyApiResult item in apiResults)
            {
                string? content = item.Content;
                List<string> snippets = new List<string>();

                if (!string.IsNullOrWhiteSpace(content))
                {
                    snippets.Add(content);
                }

                results.Add(new SearchResultItem
                {
                    Section = "web",
                    Title = item.Title ?? string.Empty,
                    Url = item.Url ?? string.Empty,
                    Description = content,
                    Snippets = snippets,
                    Score = item.Score,
                    RawContent = item.RawContent,
                    FaviconUrl = item.Favicon,
                    PublishedAt = item.PublishedDate,
                    Images = ConvertImages(item.Images)
                });
            }

            return results;
        }

        private static List<SearchImage> ConvertImages(List<TavilyApiImage>? apiImages)
        {
            List<SearchImage> images = new List<SearchImage>();
            if (apiImages == null)
            {
                return images;
            }

            foreach (TavilyApiImage image in apiImages)
            {
                string? url = image.Url ?? image.ImageUrl;
                if (!string.IsNullOrWhiteSpace(url))
                {
                    images.Add(new SearchImage
                    {
                        Url = url.Trim(),
                        Description = image.Description ?? image.Alt
                    });
                }
            }

            return images;
        }
    }
}
