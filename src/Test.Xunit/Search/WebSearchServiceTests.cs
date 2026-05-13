namespace Test.Xunit.Search
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using global::Xunit;
    using Mux.Search.Models;
    using Mux.Search.Services;

    /// <summary>
    /// Tests the normalized web-search service across provider adapters.
    /// </summary>
    public class WebSearchServiceTests
    {
        /// <summary>
        /// Verifies that Tavily responses are normalized into the shared search shape.
        /// </summary>
        [Fact]
        public async Task SearchAsync_TavilyProvider_ParsesNormalizedResponse()
        {
            string? requestBody = null;
            WebSearchService service = CreateService(
                CreateOptions(CreateProvider("tavily-primary", "tavily", "https://tavily.test/search", "tavily-key", isDefault: true)),
                request =>
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                    Assert.Equal("tavily-key", request.Headers.Authorization?.Parameter);
                    requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

                    return JsonResponse(HttpStatusCode.OK, @"{
                        ""query"": ""mux search"",
                        ""answer"": ""Mux is a coding agent."",
                        ""request_id"": ""tavily-req-1"",
                        ""response_time"": 0.42,
                        ""images"": [""https://images.example.com/mux.png""],
                        ""results"": [
                            {
                                ""title"": ""Mux Docs"",
                                ""url"": ""https://example.com/mux"",
                                ""content"": ""Mux docs snippet"",
                                ""score"": 0.91,
                                ""raw_content"": ""Full mux content"",
                                ""favicon"": ""https://example.com/favicon.ico"",
                                ""published_date"": ""2026-05-13T00:00:00Z""
                            }
                        ]
                    }");
                });

            WebSearchResponse response = await service.SearchAsync(new WebSearchRequest
            {
                Query = "mux search",
                MaxResults = 3,
                IncludeAnswer = true
            });

            Assert.NotNull(requestBody);
            Assert.Contains(@"""query"":""mux search""", requestBody!, StringComparison.Ordinal);
            Assert.Equal("tavily-primary", response.ProviderName);
            Assert.Equal("tavily", response.ProviderType);
            Assert.Equal("mux search", response.Query);
            Assert.Equal("Mux is a coding agent.", response.Answer);
            Assert.Equal("tavily-req-1", response.RequestId);
            Assert.False(response.UsedFallback);
            Assert.Single(response.ProvidersTried);
            Assert.Single(response.Attempts);
            Assert.True(response.Attempts[0].Success);
            Assert.Single(response.Images);
            Assert.Single(response.Sections["web"]);
            Assert.Equal("Mux Docs", response.Sections["web"][0].Title);
            Assert.Equal("https://example.com/mux", response.Sections["web"][0].Url);
            Assert.Equal("Mux docs snippet", response.Sections["web"][0].Description);
        }

        /// <summary>
        /// Verifies that You.com responses are normalized into the shared search shape.
        /// </summary>
        [Fact]
        public async Task SearchAsync_YouProvider_ParsesNormalizedResponse()
        {
            string? requestBody = null;
            WebSearchService service = CreateService(
                CreateOptions(CreateProvider("you-primary", "you", "https://you.test/search", "you-key", isDefault: true)),
                request =>
                {
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.True(request.Headers.TryGetValues("X-API-Key", out IEnumerable<string>? values));
                    Assert.Contains("you-key", values!);
                    requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

                    return JsonResponse(HttpStatusCode.OK, @"{
                        ""results"": {
                            ""web"": [
                                {
                                    ""title"": ""Mux Search"",
                                    ""url"": ""https://example.com/you-web"",
                                    ""description"": ""You web snippet"",
                                    ""snippets"": [""You web snippet"", ""extra detail""],
                                    ""favicon_url"": ""https://example.com/favicon.ico"",
                                    ""thumbnail_url"": ""https://example.com/thumb.png"",
                                    ""content"": ""You raw content"",
                                    ""page_age"": ""2026-05-12T00:00:00Z""
                                }
                            ],
                            ""news"": [
                                {
                                    ""title"": ""Mux News"",
                                    ""url"": ""https://example.com/you-news"",
                                    ""description"": ""News snippet"",
                                    ""snippets"": [""News snippet""]
                                }
                            ]
                        },
                        ""metadata"": {
                            ""search_uuid"": ""you-req-1"",
                            ""latency"": 0.18
                        }
                    }");
                });

            WebSearchResponse response = await service.SearchAsync(new WebSearchRequest
            {
                Query = "mux search",
                MaxResults = 4,
                Offset = 1,
                Freshness = "week"
            });

            Assert.NotNull(requestBody);
            Assert.Contains(@"""query"":""mux search""", requestBody!, StringComparison.Ordinal);
            Assert.Contains(@"""offset"":1", requestBody!, StringComparison.Ordinal);
            Assert.Equal("you-primary", response.ProviderName);
            Assert.Equal("you", response.ProviderType);
            Assert.Equal("mux search", response.Query);
            Assert.Equal("you-req-1", response.RequestId);
            Assert.Null(response.Answer);
            Assert.False(response.UsedFallback);
            Assert.Single(response.ProvidersTried);
            Assert.Single(response.Attempts);
            Assert.True(response.Attempts[0].Success);
            Assert.Single(response.Sections["web"]);
            Assert.Single(response.Sections["news"]);
            Assert.Equal("Mux Search", response.Sections["web"][0].Title);
            Assert.Equal("Mux News", response.Sections["news"][0].Title);
        }

        /// <summary>
        /// Verifies that the service falls back from a failing primary provider to a working backup.
        /// </summary>
        [Fact]
        public async Task SearchAsync_PrimaryFailure_FallsBackToBackupProvider()
        {
            int tavilyRequests = 0;
            int youRequests = 0;
            WebSearchService service = CreateService(
                new WebSearchServiceOptions
                {
                    Enabled = true,
                    AllowFallback = true,
                    Providers = new List<WebSearchProviderRegistration>
                    {
                        CreateProvider("tavily-primary", "tavily", "https://tavily.test/search", "tavily-key", isDefault: true),
                        CreateProvider("you-backup", "you", "https://you.test/search", "you-key")
                    }
                },
                request =>
                {
                    if (string.Equals(request.RequestUri?.Host, "tavily.test", StringComparison.OrdinalIgnoreCase))
                    {
                        tavilyRequests++;
                        return JsonResponse(HttpStatusCode.ServiceUnavailable, @"{ ""error"": ""temporarily unavailable"" }");
                    }

                    youRequests++;
                    return JsonResponse(HttpStatusCode.OK, @"{
                        ""results"": {
                            ""web"": [
                                {
                                    ""title"": ""Backup Result"",
                                    ""url"": ""https://example.com/backup"",
                                    ""description"": ""backup snippet"",
                                    ""snippets"": [""backup snippet""]
                                }
                            ]
                        },
                        ""metadata"": {
                            ""search_uuid"": ""you-backup-1"",
                            ""latency"": 0.25
                        }
                    }");
                });

            WebSearchResponse response = await service.SearchAsync(new WebSearchRequest
            {
                Query = "mux fallback"
            });

            Assert.Equal(1, tavilyRequests);
            Assert.Equal(1, youRequests);
            Assert.Equal("you-backup", response.ProviderName);
            Assert.Equal("you", response.ProviderType);
            Assert.True(response.UsedFallback);
            Assert.Equal(new[] { "tavily-primary", "you-backup" }, response.ProvidersTried);
            Assert.Equal(2, response.Attempts.Count);
            Assert.False(response.Attempts[0].Success);
            Assert.True(response.Attempts[1].Success);
            Assert.Contains("status code 503", response.Attempts[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Single(response.Sections["web"]);
            Assert.Equal("Backup Result", response.Sections["web"][0].Title);
        }

        private static WebSearchService CreateService(
            WebSearchServiceOptions options,
            Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            return new WebSearchService(
                options,
                _ => new HttpClient(new StubHttpMessageHandler(responder), disposeHandler: true));
        }

        private static WebSearchServiceOptions CreateOptions(params WebSearchProviderRegistration[] providers)
        {
            return new WebSearchServiceOptions
            {
                Enabled = true,
                AllowFallback = true,
                Providers = providers.ToList()
            };
        }

        private static WebSearchProviderRegistration CreateProvider(
            string name,
            string providerType,
            string endpoint,
            string apiKey,
            bool isDefault = false)
        {
            return new WebSearchProviderRegistration
            {
                Name = name,
                ProviderType = providerType,
                Enabled = true,
                IsDefault = isDefault,
                Options = new SearchProviderOptions
                {
                    Endpoint = endpoint,
                    ApiKey = apiKey,
                    Timeout = TimeSpan.FromSeconds(10)
                }
            };
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _Responder;

            public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            {
                _Responder = responder ?? throw new ArgumentNullException(nameof(responder));
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_Responder(request));
            }
        }
    }
}
