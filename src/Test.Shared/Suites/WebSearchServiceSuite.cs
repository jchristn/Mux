namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Models;
    using Mux.Search.Services;
    using Test.Shared.Search;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="WebSearchService"/> across provider adapters. Ported from the
    /// <c>WebSearchServiceTests</c> xUnit suite.
    /// </summary>
    public static class WebSearchServiceSuite
    {
        /// <summary>
        /// Builds the web-search-service suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the web-search-service cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "WebSearchService",
                "Normalized web-search service across providers",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("WebSearchService", "TavilyProviderParsesNormalizedResponse", "Tavily responses are normalized into the shared search shape", async (CancellationToken ct) =>
                    {
                        string? requestBody = null;
                        WebSearchService service = CreateService(
                            CreateOptions(CreateProvider("tavily-primary", "tavily", "https://tavily.test/search", "tavily-key", isDefault: true)),
                            request =>
                            {
                                MuxAssert.AreEqual(HttpMethod.Post, request.Method, "method");
                                MuxAssert.AreEqual("Bearer", request.Headers.Authorization?.Scheme, "auth scheme");
                                MuxAssert.AreEqual("tavily-key", request.Headers.Authorization?.Parameter, "auth parameter");
                                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                                return JsonResponse(HttpStatusCode.OK, "{\"query\":\"mux search\",\"answer\":\"Mux is a coding agent.\",\"request_id\":\"tavily-req-1\",\"response_time\":0.42,\"images\":[\"https://images.example.com/mux.png\"],\"results\":[{\"title\":\"Mux Docs\",\"url\":\"https://example.com/mux\",\"content\":\"Mux docs snippet\",\"score\":0.91,\"raw_content\":\"Full mux content\",\"favicon\":\"https://example.com/favicon.ico\",\"published_date\":\"2026-05-13T00:00:00Z\"}]}");
                            });

                        WebSearchResponse response = await service.SearchAsync(new WebSearchRequest { Query = "mux search", MaxResults = 3, IncludeAnswer = true }).ConfigureAwait(false);

                        MuxAssert.IsNotNull(requestBody, "request body");
                        MuxAssert.Contains("\"query\":\"mux search\"", requestBody!, "query in body");
                        MuxAssert.AreEqual("tavily-primary", response.ProviderName, "provider name");
                        MuxAssert.AreEqual("tavily", response.ProviderType, "provider type");
                        MuxAssert.AreEqual("mux search", response.Query, "query");
                        MuxAssert.AreEqual("Mux is a coding agent.", response.Answer, "answer");
                        MuxAssert.AreEqual("tavily-req-1", response.RequestId, "request id");
                        MuxAssert.IsFalse(response.UsedFallback, "used fallback");
                        MuxAssert.AreEqual(1, response.ProvidersTried.Count, "providers tried");
                        MuxAssert.AreEqual(1, response.Attempts.Count, "attempts");
                        MuxAssert.IsTrue(response.Attempts[0].Success, "attempt success");
                        MuxAssert.AreEqual(1, response.Images.Count, "images");
                        MuxAssert.AreEqual(1, response.Sections["web"].Count, "web section count");
                        MuxAssert.AreEqual("Mux Docs", response.Sections["web"][0].Title, "web title");
                        MuxAssert.AreEqual("https://example.com/mux", response.Sections["web"][0].Url, "web url");
                        MuxAssert.AreEqual("Mux docs snippet", response.Sections["web"][0].Description, "web description");
                    }),

                    new TestCaseDescriptor("WebSearchService", "YouProviderParsesNormalizedResponse", "You.com responses are normalized into the shared search shape", async (CancellationToken ct) =>
                    {
                        string? requestBody = null;
                        WebSearchService service = CreateService(
                            CreateOptions(CreateProvider("you-primary", "you", "https://you.test/search", "you-key", isDefault: true)),
                            request =>
                            {
                                MuxAssert.AreEqual(HttpMethod.Post, request.Method, "method");
                                MuxAssert.IsTrue(request.Headers.TryGetValues("X-API-Key", out IEnumerable<string>? values), "X-API-Key present");
                                MuxAssert.IsTrue(values!.Contains("you-key"), "X-API-Key value");
                                requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                                return JsonResponse(HttpStatusCode.OK, "{\"results\":{\"web\":[{\"title\":\"Mux Search\",\"url\":\"https://example.com/you-web\",\"description\":\"You web snippet\",\"snippets\":[\"You web snippet\",\"extra detail\"],\"favicon_url\":\"https://example.com/favicon.ico\",\"thumbnail_url\":\"https://example.com/thumb.png\",\"content\":\"You raw content\",\"page_age\":\"2026-05-12T00:00:00Z\"}],\"news\":[{\"title\":\"Mux News\",\"url\":\"https://example.com/you-news\",\"description\":\"News snippet\",\"snippets\":[\"News snippet\"]}]},\"metadata\":{\"search_uuid\":\"you-req-1\",\"latency\":0.18}}");
                            });

                        WebSearchResponse response = await service.SearchAsync(new WebSearchRequest { Query = "mux search", MaxResults = 4, Offset = 1, Freshness = "week" }).ConfigureAwait(false);

                        MuxAssert.IsNotNull(requestBody, "request body");
                        MuxAssert.Contains("\"query\":\"mux search\"", requestBody!, "query in body");
                        MuxAssert.Contains("\"offset\":1", requestBody!, "offset in body");
                        MuxAssert.AreEqual("you-primary", response.ProviderName, "provider name");
                        MuxAssert.AreEqual("you", response.ProviderType, "provider type");
                        MuxAssert.AreEqual("mux search", response.Query, "query");
                        MuxAssert.AreEqual("you-req-1", response.RequestId, "request id");
                        MuxAssert.IsNull(response.Answer, "answer null");
                        MuxAssert.IsFalse(response.UsedFallback, "used fallback");
                        MuxAssert.AreEqual(1, response.ProvidersTried.Count, "providers tried");
                        MuxAssert.AreEqual(1, response.Attempts.Count, "attempts");
                        MuxAssert.IsTrue(response.Attempts[0].Success, "attempt success");
                        MuxAssert.AreEqual(1, response.Sections["web"].Count, "web section count");
                        MuxAssert.AreEqual(1, response.Sections["news"].Count, "news section count");
                        MuxAssert.AreEqual("Mux Search", response.Sections["web"][0].Title, "web title");
                        MuxAssert.AreEqual("Mux News", response.Sections["news"][0].Title, "news title");
                    }),

                    new TestCaseDescriptor("WebSearchService", "PrimaryFailureFallsBackToBackupProvider", "The service falls back from a failing primary to a working backup", async (CancellationToken ct) =>
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
                                    return JsonResponse(HttpStatusCode.ServiceUnavailable, "{ \"error\": \"temporarily unavailable\" }");
                                }

                                youRequests++;
                                return JsonResponse(HttpStatusCode.OK, "{\"results\":{\"web\":[{\"title\":\"Backup Result\",\"url\":\"https://example.com/backup\",\"description\":\"backup snippet\",\"snippets\":[\"backup snippet\"]}]},\"metadata\":{\"search_uuid\":\"you-backup-1\",\"latency\":0.25}}");
                            });

                        WebSearchResponse response = await service.SearchAsync(new WebSearchRequest { Query = "mux fallback" }).ConfigureAwait(false);

                        MuxAssert.AreEqual(1, tavilyRequests, "tavily requests");
                        MuxAssert.AreEqual(1, youRequests, "you requests");
                        MuxAssert.AreEqual("you-backup", response.ProviderName, "provider name");
                        MuxAssert.AreEqual("you", response.ProviderType, "provider type");
                        MuxAssert.IsTrue(response.UsedFallback, "used fallback");
                        MuxAssert.AreEqual(2, response.ProvidersTried.Count, "providers tried count");
                        MuxAssert.AreEqual("tavily-primary", response.ProvidersTried[0], "providers tried 0");
                        MuxAssert.AreEqual("you-backup", response.ProvidersTried[1], "providers tried 1");
                        MuxAssert.AreEqual(2, response.Attempts.Count, "attempts");
                        MuxAssert.IsFalse(response.Attempts[0].Success, "attempt 0 failure");
                        MuxAssert.IsTrue(response.Attempts[1].Success, "attempt 1 success");
                        MuxAssert.Contains("status code 503", response.Attempts[0].Message?.ToLowerInvariant(), "503 message");
                        MuxAssert.AreEqual(1, response.Sections["web"].Count, "web section count");
                        MuxAssert.AreEqual("Backup Result", response.Sections["web"][0].Title, "web title");
                    })
                });
        }

        private static WebSearchService CreateService(WebSearchServiceOptions options, Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            return new WebSearchService(options, _ => new HttpClient(new StubHttpMessageHandler(responder), disposeHandler: true));
        }

        private static WebSearchServiceOptions CreateOptions(params WebSearchProviderRegistration[] providers)
        {
            return new WebSearchServiceOptions { Enabled = true, AllowFallback = true, Providers = providers.ToList() };
        }

        private static WebSearchProviderRegistration CreateProvider(string name, string providerType, string endpoint, string apiKey, bool isDefault = false)
        {
            return new WebSearchProviderRegistration
            {
                Name = name,
                ProviderType = providerType,
                Enabled = true,
                IsDefault = isDefault,
                Options = new SearchProviderOptions { Endpoint = endpoint, ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(10) }
            };
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        {
            return new HttpResponseMessage(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
