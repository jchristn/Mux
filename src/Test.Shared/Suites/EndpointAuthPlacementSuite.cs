namespace Test.Shared.Suites
{
    using System;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Utility;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the endpoint auth-placement model: the <see cref="AuthPlacementEnum"/> converter,
    /// its persistence on <see cref="EndpointConfig"/>, and the <see cref="QueryStringAuthHandler"/> that
    /// carries a query-string credential onto outbound requests.
    /// </summary>
    public static class EndpointAuthPlacementSuite
    {
        /// <summary>
        /// Builds the auth-placement suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the auth-placement cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "EndpointAuthPlacement",
                "Endpoint auth placement: enum, persistence, and query-string handler",
                new System.Collections.Generic.List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("EndpointAuthPlacement", "DefaultsToBearer", "AuthPlacement defaults to Bearer and AuthParameterName to null", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig();
                        MuxAssert.AreEqual(AuthPlacementEnum.Bearer, config.AuthPlacement, "default placement");
                        MuxAssert.IsNull(config.AuthParameterName, "default parameter name");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "RoundTripsPlacementAndParameterName", "AuthPlacement and AuthParameterName survive a JSON round-trip", (CancellationToken ct) =>
                    {
                        EndpointConfig original = new EndpointConfig
                        {
                            Name = "querystring-endpoint",
                            AdapterType = AdapterTypeEnum.OpenAiCompatible,
                            BaseUrl = "https://api.example.com/v1",
                            Model = "some-model",
                            ApiKey = "secret-key",
                            AuthPlacement = AuthPlacementEnum.Query,
                            AuthParameterName = "api_key"
                        };

                        string json = JsonSerializer.Serialize(original);
                        MuxAssert.Contains("\"authPlacement\":\"query\"", json, "placement serialized as string");
                        MuxAssert.Contains("\"authParameterName\":\"api_key\"", json, "parameter name serialized");

                        EndpointConfig? deserialized = JsonSerializer.Deserialize<EndpointConfig>(json);
                        MuxAssert.IsNotNull(deserialized, "deserialized");
                        MuxAssert.AreEqual(AuthPlacementEnum.Query, deserialized!.AuthPlacement, "placement");
                        MuxAssert.AreEqual("api_key", deserialized.AuthParameterName, "parameter name");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "ParameterNameOmittedWhenNull", "AuthParameterName is omitted from JSON when null", (CancellationToken ct) =>
                    {
                        EndpointConfig config = new EndpointConfig { Name = "n", BaseUrl = "u", Model = "m" };
                        string json = JsonSerializer.Serialize(config);
                        MuxAssert.DoesNotContain("authParameterName", json, "omitted when null");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "CloneCopiesPlacement", "Clone copies AuthPlacement and AuthParameterName", (CancellationToken ct) =>
                    {
                        EndpointConfig original = new EndpointConfig
                        {
                            Name = "src",
                            AdapterType = AdapterTypeEnum.OpenAi,
                            BaseUrl = "https://api.example.com/v1",
                            Model = "m",
                            AuthPlacement = AuthPlacementEnum.Header,
                            AuthParameterName = "x-api-key"
                        };

                        EndpointConfig clone = original.Clone();
                        MuxAssert.AreEqual(AuthPlacementEnum.Header, clone.AuthPlacement, "placement copied");
                        MuxAssert.AreEqual("x-api-key", clone.AuthParameterName, "parameter name copied");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "ConverterEmptyStringDefaultsToBearer", "An empty placement string reads as Bearer", (CancellationToken ct) =>
                    {
                        EndpointConfig? config = JsonSerializer.Deserialize<EndpointConfig>("{\"name\":\"n\",\"baseUrl\":\"u\",\"model\":\"m\",\"authPlacement\":\"\"}");
                        MuxAssert.IsNotNull(config, "config");
                        MuxAssert.AreEqual(AuthPlacementEnum.Bearer, config!.AuthPlacement, "empty defaults to bearer");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "ConverterAcceptsAliases", "The converter accepts hyphen/underscore aliases", (CancellationToken ct) =>
                    {
                        EndpointConfig? header = JsonSerializer.Deserialize<EndpointConfig>("{\"name\":\"n\",\"baseUrl\":\"u\",\"model\":\"m\",\"authPlacement\":\"custom-header\"}");
                        EndpointConfig? query = JsonSerializer.Deserialize<EndpointConfig>("{\"name\":\"n\",\"baseUrl\":\"u\",\"model\":\"m\",\"authPlacement\":\"query_param\"}");
                        MuxAssert.AreEqual(AuthPlacementEnum.Header, header!.AuthPlacement, "custom-header alias");
                        MuxAssert.AreEqual(AuthPlacementEnum.Query, query!.AuthPlacement, "query_param alias");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "ConverterRejectsUnknown", "The converter throws on an unknown placement", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<JsonException>(
                            () => JsonSerializer.Deserialize<EndpointConfig>("{\"name\":\"n\",\"baseUrl\":\"u\",\"model\":\"m\",\"authPlacement\":\"sigv4\"}"),
                            "unknown placement");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "QueryHandlerAppendsParameter", "The query-string handler appends the credential to a URI with no query", async (CancellationToken ct) =>
                    {
                        CapturingHandler capture = new CapturingHandler();
                        QueryStringAuthHandler handler = new QueryStringAuthHandler("api_key", "secret");
                        handler.InnerHandler = capture;
                        using (HttpClient client = new HttpClient(handler))
                        {
                            await client.GetAsync("https://api.example.com/v1/models", ct).ConfigureAwait(false);
                        }

                        MuxAssert.IsNotNull(capture.LastUri, "captured uri");
                        MuxAssert.Contains("api_key=secret", capture.LastUri!.Query, "credential appended");
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "QueryHandlerPreservesExistingParameters", "The query-string handler preserves other query parameters", async (CancellationToken ct) =>
                    {
                        CapturingHandler capture = new CapturingHandler();
                        QueryStringAuthHandler handler = new QueryStringAuthHandler("api_key", "secret");
                        handler.InnerHandler = capture;
                        using (HttpClient client = new HttpClient(handler))
                        {
                            await client.GetAsync("https://api.example.com/v1/models?verbose=true", ct).ConfigureAwait(false);
                        }

                        MuxAssert.Contains("verbose=true", capture.LastUri!.Query, "existing parameter preserved");
                        MuxAssert.Contains("api_key=secret", capture.LastUri!.Query, "credential appended");
                    }),

                    new TestCaseDescriptor("EndpointAuthPlacement", "QueryHandlerDoesNotOverwriteExplicitValue", "The query-string handler does not overwrite an explicit value on the URI", async (CancellationToken ct) =>
                    {
                        CapturingHandler capture = new CapturingHandler();
                        QueryStringAuthHandler handler = new QueryStringAuthHandler("api_key", "secret");
                        handler.InnerHandler = capture;
                        using (HttpClient client = new HttpClient(handler))
                        {
                            await client.GetAsync("https://api.example.com/v1/models?api_key=explicit", ct).ConfigureAwait(false);
                        }

                        MuxAssert.Contains("api_key=explicit", capture.LastUri!.Query, "explicit value preserved");
                        MuxAssert.DoesNotContain("secret", capture.LastUri!.Query, "handler value not added");
                    })
                });
        }

        // Captures the request URI reaching the transport and returns an empty 200 without any network I/O.
        private sealed class CapturingHandler : HttpMessageHandler
        {
            public Uri? LastUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }
}
