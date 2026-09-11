namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Telemetry;
    using Mux.Server;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite that boots a real <see cref="MuxServer"/> with a usage-telemetry query service over a
    /// seeded store, then exercises the <c>/v1.0/api/usage/*</c> endpoints over HTTP: summary totals, the
    /// filters list, and the paginated event history. Verifies the read path end to end, including cost
    /// derivation from the pricing table.
    /// </summary>
    public static class UsageRoutesSuite
    {
        /// <summary>
        /// Builds the usage-routes suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "UsageRoutes",
                "Live MuxServer usage-telemetry endpoints over HTTP",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("UsageRoutes", "UsageEndpointsReturnSeededData", "Summary, filters, and events reflect seeded telemetry", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux_usageapi_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        string dbPath = Path.Combine(dir, "usage.db");
                        string sessions = Path.Combine(dir, "sessions");

                        try
                        {
                            using SqliteUsageStore store = new SqliteUsageStore(dbPath, 90, 5_000_000);
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            store.InsertBatchAsync(new List<UsageEvent>
                            {
                                Event(now - 1000, "gpt-4o", "openai-endpoint", 1000, 0, 500),
                                Event(now - 2000, "gpt-4o", "openai-endpoint", 2000, 0, 800),
                                Event(now - 3000, "claude-opus-4-8", "anthropic-endpoint", 500, 0, 300)
                            }, ct).GetAwaiter().GetResult();

                            PricingTable pricing = new PricingTable
                            {
                                Version = "test",
                                Models = new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
                                {
                                    ["gpt-4o"] = new ModelPricing(2.5, 1.25, 10.0)
                                }
                            };
                            UsageQueryService query = new UsageQueryService(store, () => pricing);

                            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = null };

                            MuxServer? server = null;
                            int port = 0;
                            for (int attempt = 0; attempt < 10 && server == null; attempt++)
                            {
                                port = FreeLoopbackPort();
                                rest.Port = port;
                                MuxServer candidate = new MuxServer(rest, "9.9.9-test", new SessionStore(sessions), () => new List<EndpointConfig>(), null, query, null);
                                try { candidate.Start(); server = candidate; }
                                catch (Exception) { candidate.Dispose(); Thread.Sleep(50); }
                            }

                            MuxAssert.IsNotNull(server, "server bound to a loopback port");
                            string baseUrl = "http://127.0.0.1:" + port;

                            try
                            {
                                using HttpClient http = new HttpClient();
                                http.Timeout = TimeSpan.FromSeconds(5);

                                // Summary over a wide window: all three calls, tokens summed, cost from gpt-4o only.
                                string summaryBody = GetWithRetry(http, baseUrl + "/v1.0/api/usage/summary?range=all");
                                using JsonDocument summary = JsonDocument.Parse(summaryBody);
                                System.Text.Json.JsonElement metrics = summary.RootElement.GetProperty("Metrics");
                                MuxAssert.AreEqual(3, metrics.GetProperty("Calls").GetInt64(), "three calls counted");
                                MuxAssert.AreEqual(3500L, metrics.GetProperty("InputTokens").GetInt64(), "input tokens summed");
                                MuxAssert.IsTrue(metrics.GetProperty("CostUsd").GetDouble() > 0, "cost derived from pricing");

                                // Filters: distinct endpoints and models present.
                                string filtersBody = http.GetAsync(baseUrl + "/v1.0/api/usage/filters").GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                using JsonDocument filters = JsonDocument.Parse(filtersBody);
                                MuxAssert.IsTrue(filters.RootElement.GetProperty("Enabled").GetBoolean(), "telemetry enabled");
                                MuxAssert.AreEqual(2, filters.RootElement.GetProperty("Models").GetArrayLength(), "two distinct models");

                                // Events: paginated history returns all three, newest first.
                                string eventsBody = http.GetAsync(baseUrl + "/v1.0/api/usage/events?range=all&page=1&pageSize=25").GetAwaiter().GetResult().Content.ReadAsStringAsync().GetAwaiter().GetResult();
                                using JsonDocument events = JsonDocument.Parse(eventsBody);
                                MuxAssert.AreEqual(3L, events.RootElement.GetProperty("TotalCount").GetInt64(), "three events total");
                                MuxAssert.AreEqual(3, events.RootElement.GetProperty("Items").GetArrayLength(), "three rows on the page");
                            }
                            finally
                            {
                                server!.Dispose();
                            }
                        }
                        finally
                        {
                            TryDeleteDirectory(dir);
                        }

                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static UsageEvent Event(long ts, string model, string endpoint, int input, int cached, int output)
        {
            return new UsageEvent
            {
                TimestampUnixMs = ts,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = endpoint,
                AdapterType = model.StartsWith("gpt", StringComparison.Ordinal) ? "openai" : "anthropic",
                Model = model,
                InputTokens = input,
                CachedTokens = cached,
                OutputTokens = output,
                TotalTokens = input + output,
                TimeToFirstTokenMs = 40,
                StreamingMs = 200,
                TotalMs = 240,
                TokensPerSecond = 10.0,
                Success = true
            };
        }

        private static string GetWithRetry(HttpClient http, string url)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    HttpResponseMessage r = http.GetAsync(url).GetAwaiter().GetResult();
                    if (r.IsSuccessStatusCode)
                    {
                        return r.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception)
                {
                }

                Thread.Sleep(50);
            }

            return string.Empty;
        }

        private static int FreeLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        #endregion
    }
}
