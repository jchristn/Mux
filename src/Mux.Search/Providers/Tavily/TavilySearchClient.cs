namespace Mux.Search.Providers.Tavily
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Internal;
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

            SearchProviderResponse providerResponse =
                await SendAsync(request, cancellationToken).ConfigureAwait(false);

            using (providerResponse)
            {
                JsonElement root = providerResponse.Document.RootElement;
                TavilySearchResponse response = new TavilySearchResponse
                {
                    ProviderName = ProviderName,
                    Query = root.GetStringOrNull("query") ?? query.Query,
                    Answer = root.GetStringOrNull("answer"),
                    RequestId = root.GetStringOrNull("request_id"),
                    LatencySeconds = root.GetDoubleOrNull("response_time"),
                    Images = ParseImages(root.GetPropertyOrNull("images")),
                    RawJson = providerResponse.RawJson
                };

                response.SetSection("web", ParseResults(root.GetPropertyOrNull("results")));

                JsonElement? autoParameters = root.GetPropertyOrNull("auto_parameters");
                if (autoParameters.HasValue && autoParameters.Value.ValueKind == JsonValueKind.Object)
                {
                    response.AutoParameters = new TavilyAutoParameters
                    {
                        Topic = autoParameters.Value.GetStringOrNull("topic"),
                        SearchDepth = autoParameters.Value.GetStringOrNull("search_depth")
                    };
                }

                JsonElement? usage = root.GetPropertyOrNull("usage");
                if (usage.HasValue && usage.Value.ValueKind == JsonValueKind.Object)
                {
                    response.Usage = new TavilyUsage
                    {
                        CreditsUsed = usage.Value.GetInt32OrNull("credits_used")
                            ?? usage.Value.GetInt32OrNull("credits")
                    };
                }

                return response;
            }
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

        private static List<SearchResultItem> ParseResults(JsonElement? resultsElement)
        {
            List<SearchResultItem> results = new List<SearchResultItem>();
            if (!resultsElement.HasValue || resultsElement.Value.ValueKind != JsonValueKind.Array)
            {
                return results;
            }

            foreach (JsonElement item in resultsElement.Value.EnumerateArray())
            {
                string? content = item.GetStringOrNull("content");
                List<string> snippets = new List<string>();

                if (!string.IsNullOrWhiteSpace(content))
                {
                    snippets.Add(content);
                }

                results.Add(new SearchResultItem
                {
                    Section = "web",
                    Title = item.GetStringOrNull("title") ?? string.Empty,
                    Url = item.GetStringOrNull("url") ?? string.Empty,
                    Description = content,
                    Snippets = snippets,
                    Score = item.GetDoubleOrNull("score"),
                    RawContent = item.GetStringOrNull("raw_content"),
                    FaviconUrl = item.GetStringOrNull("favicon"),
                    PublishedAt = item.GetDateTimeOffsetOrNull("published_date"),
                    Images = ParseImages(item.GetPropertyOrNull("images"))
                });
            }

            return results;
        }

        private static List<SearchImage> ParseImages(JsonElement? imagesElement)
        {
            List<SearchImage> images = new List<SearchImage>();
            if (!imagesElement.HasValue || imagesElement.Value.ValueKind != JsonValueKind.Array)
            {
                return images;
            }

            foreach (JsonElement image in imagesElement.Value.EnumerateArray())
            {
                if (image.ValueKind == JsonValueKind.String)
                {
                    string? url = image.GetString();
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        images.Add(new SearchImage { Url = url.Trim() });
                    }

                    continue;
                }

                if (image.ValueKind == JsonValueKind.Object)
                {
                    string? url = image.GetStringOrNull("url")
                        ?? image.GetStringOrNull("image_url");

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        images.Add(new SearchImage
                        {
                            Url = url.Trim(),
                            Description = image.GetStringOrNull("description")
                                ?? image.GetStringOrNull("alt")
                        });
                    }
                }
            }

            return images;
        }
    }
}
