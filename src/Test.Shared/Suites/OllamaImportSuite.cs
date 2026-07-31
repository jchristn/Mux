namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Utility;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Ollama completion-model importer. Covers base-URL normalization (the
    /// value stored per imported endpoint) and live model discovery against an in-process Ollama-style
    /// <c>/api/tags</c> server.
    /// </summary>
    public static class OllamaImportSuite
    {
        private const string SuiteId = "OllamaImport";

        /// <summary>
        /// Builds the Ollama-import suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for Ollama-import cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Ollama base-URL normalization and completion-model discovery",
                new List<TestCaseDescriptor>
                {
                    Case("NormalizePrependsScheme", "A scheme-less host gets http:// and keeps its port", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("http://localhost:11434", OllamaModelLister.NormalizeBaseUrl("localhost:11434"), "scheme added");
                        return Task.CompletedTask;
                    }),

                    Case("NormalizeStripsV1AndSlash", "A trailing /v1 and slashes are removed", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("http://localhost:11434", OllamaModelLister.NormalizeBaseUrl("http://localhost:11434/v1/"), "v1 + slash stripped");
                        MuxAssert.AreEqual("https://host:9/api", OllamaModelLister.NormalizeBaseUrl("https://host:9/api/"), "non-v1 path preserved");
                        return Task.CompletedTask;
                    }),

                    Case("NormalizeBlankIsEmpty", "A null or blank URL normalizes to empty", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(string.Empty, OllamaModelLister.NormalizeBaseUrl(null), "null -> empty");
                        MuxAssert.AreEqual(string.Empty, OllamaModelLister.NormalizeBaseUrl("   "), "blank -> empty");
                        return Task.CompletedTask;
                    }),

                    Case("ListModelsReturnsSortedNames", "Discovery returns every model name, sorted", async (CancellationToken ct) =>
                    {
                        using (LocalLlmTestServer server = LocalLlmTestServer.Start())
                        {
                            List<string> models = await OllamaModelLister
                                .ListModelsAsync(server.Endpoint, ignoreCertErrors: false, ct)
                                .ConfigureAwait(false);

                            MuxAssert.AreEqual(3, models.Count, "all three tags returned");
                            MuxAssert.AreEqual("llama3:latest", models[0], "sorted first");
                            MuxAssert.AreEqual("nomic-embed-text:latest", models[1], "sorted second");
                            MuxAssert.AreEqual("qwen2.5-coder:7b", models[2], "sorted third");

                            // The importer must hit Ollama's native tags endpoint at the server root.
                            MuxAssert.Contains("/api/tags", string.Join(",", server.RequestPaths), "queried /api/tags");
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
