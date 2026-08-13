namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Utility;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for live model enumeration: OpenAI-compatible <c>/v1/models</c> discovery
    /// (<see cref="OpenAiModelLister"/>) and the per-adapter dispatch plus failure capture in
    /// <see cref="EndpointModelLister"/>. Discovery runs against the in-process
    /// <see cref="LocalLlmTestServer"/>.
    /// </summary>
    public static class EndpointModelListerSuite
    {
        private const string SuiteId = "EndpointModelLister";

        /// <summary>
        /// Builds the endpoint-model-lister suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the model-enumeration cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Live model enumeration across OpenAI-compatible and Ollama backends",
                new List<TestCaseDescriptor>
                {
                    Case("NormalizeAppendsModels", "A /v1 base URL yields the conventional /v1/models path", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("http://host:9/v1/models", OpenAiModelLister.NormalizeModelsUrl("http://host:9/v1/"), "models appended");
                        MuxAssert.AreEqual("http://host:9/v1/models", OpenAiModelLister.NormalizeModelsUrl("http://host:9/v1/models"), "already-terminal path preserved");
                        MuxAssert.AreEqual(string.Empty, OpenAiModelLister.NormalizeModelsUrl("   "), "blank -> empty");
                        return Task.CompletedTask;
                    }),

                    Case("OpenAiListReturnsSortedIds", "OpenAI discovery returns every model id, sorted", async (CancellationToken ct) =>
                    {
                        using (LocalLlmTestServer server = LocalLlmTestServer.Start())
                        {
                            List<string> models = await OpenAiModelLister
                                .ListModelsAsync(server.Endpoint + "/v1", null, ignoreCertErrors: false, ct)
                                .ConfigureAwait(false);

                            MuxAssert.AreEqual(3, models.Count, "all three ids returned");
                            MuxAssert.AreEqual("gpt-4o", models[0], "sorted first");
                            MuxAssert.AreEqual("gpt-4o-mini", models[1], "sorted second");
                            MuxAssert.AreEqual("o1-preview", models[2], "sorted third");
                            MuxAssert.Contains("/v1/models", string.Join(",", server.RequestPaths), "queried /v1/models");
                        }
                    }),

                    Case("OpenAiListSendsAuthHeader", "Configured headers are sent on the discovery request", async (CancellationToken ct) =>
                    {
                        using (LocalLlmTestServer server = LocalLlmTestServer.Start())
                        {
                            Dictionary<string, string> headers = new Dictionary<string, string>
                            {
                                { "Authorization", "Bearer sk-test-123" }
                            };

                            await OpenAiModelLister
                                .ListModelsAsync(server.Endpoint + "/v1", headers, ignoreCertErrors: false, ct)
                                .ConfigureAwait(false);

                            MuxAssert.AreEqual("Bearer sk-test-123", server.HeaderValue("Authorization"), "auth header forwarded");
                        }
                    }),

                    Case("DispatchOllamaUsesTags", "An Ollama endpoint is enumerated via /api/tags", async (CancellationToken ct) =>
                    {
                        using (LocalLlmTestServer server = LocalLlmTestServer.Start())
                        {
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "ollama-local",
                                AdapterType = AdapterTypeEnum.Ollama,
                                BaseUrl = server.Endpoint,
                                Model = "llama3:latest"
                            };

                            EndpointModelListResult result = await EndpointModelLister
                                .ListModelsAsync(endpoint, ignoreCertErrors: false, ct)
                                .ConfigureAwait(false);

                            MuxAssert.IsTrue(result.Success, "success");
                            MuxAssert.AreEqual(3, result.Models.Count, "three ollama models");
                            MuxAssert.Contains("/api/tags", string.Join(",", server.RequestPaths), "queried /api/tags");
                        }
                    }),

                    Case("DispatchOpenAiUsesModels", "An OpenAI endpoint is enumerated via /v1/models", async (CancellationToken ct) =>
                    {
                        using (LocalLlmTestServer server = LocalLlmTestServer.Start())
                        {
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "openai-prod",
                                AdapterType = AdapterTypeEnum.OpenAi,
                                BaseUrl = server.Endpoint + "/v1",
                                Model = "gpt-4o"
                            };

                            EndpointModelListResult result = await EndpointModelLister
                                .ListModelsAsync(endpoint, ignoreCertErrors: false, ct)
                                .ConfigureAwait(false);

                            MuxAssert.IsTrue(result.Success, "success");
                            MuxAssert.AreEqual(3, result.Models.Count, "three openai models");
                            MuxAssert.Contains("/v1/models", string.Join(",", server.RequestPaths), "queried /v1/models");
                        }
                    }),

                    Case("DispatchCapturesBackendFailure", "A 404 from the backend is captured, not thrown", async (CancellationToken ct) =>
                    {
                        using (LocalLlmTestServer server = LocalLlmTestServer.Start())
                        {
                            // No trailing /v1, so the request targets {root}/models, which the server 404s.
                            EndpointConfig endpoint = new EndpointConfig
                            {
                                Name = "broken",
                                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                                BaseUrl = server.Endpoint,
                                Model = "whatever"
                            };

                            EndpointModelListResult result = await EndpointModelLister
                                .ListModelsAsync(endpoint, ignoreCertErrors: false, ct)
                                .ConfigureAwait(false);

                            MuxAssert.IsFalse(result.Success, "failure captured");
                            MuxAssert.AreEqual("models_endpoint_not_found", result.ErrorCode, "404 classified");
                            MuxAssert.AreEqual(0, result.Models.Count, "no models on failure");
                        }
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }
    }
}
