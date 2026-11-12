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
                    new TestCaseDescriptor("UsageRoutes", "UsageEndpointsReturnSeededData", "Summary, filters, events, every range of the time series, the split series, and failure reasons reflect seeded telemetry", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux_usageapi_" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);
                        string dbPath = Path.Combine(dir, "usage.db");
                        string sessions = Path.Combine(dir, "sessions");

                        try
                        {
                            using SqliteUsageStore store = new SqliteUsageStore(dbPath, 90, 5_000_000);
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            UsageEvent failed = Event(now - 3000, "claude-opus-4-8", "anthropic-endpoint", 500, 0, 300);
                            failed.Success = false;
                            failed.ErrorCode = "http_429";
                            store.InsertBatchAsync(new List<UsageEvent>
                            {
                                Event(now - 1000, "gpt-4o", "openai-endpoint", 1000, 0, 500),
                                Event(now - 2000, "gpt-4o", "openai-endpoint", 2000, 0, 800),
                                failed
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

                                // Time series for every range keyword: calls and errors per bucket sum to the totals.
                                foreach (string range in new[] { "hour", "day", "week", "month", "quarter", "year", "all" })
                                {
                                    using JsonDocument series = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/timeseries?range=" + range));
                                    long calls = 0, errors = 0;
                                    foreach (JsonElement bucket in series.RootElement.GetProperty("Items").EnumerateArray())
                                    {
                                        calls += bucket.GetProperty("Metrics").GetProperty("Calls").GetInt64();
                                        errors += bucket.GetProperty("Metrics").GetProperty("Errors").GetInt64();
                                    }

                                    MuxAssert.AreEqual(3L, calls, "calls in the " + range + " series");
                                    MuxAssert.AreEqual(1L, errors, "errors in the " + range + " series");
                                    int expected = range == "quarter" ? 91 : range == "year" ? 52 : 0;
                                    if (expected > 0)
                                    {
                                        MuxAssert.AreEqual(expected, series.RootElement.GetProperty("Count").GetInt32(), range + " bucket count");
                                    }
                                }

                                // Heatmap for every range: one value per cell of the shared layout, every call counted, the
                                // browser's offset applied with JavaScript's sign, and the filters honored.
                                foreach (string range in new[] { "hour", "day", "week", "month", "quarter", "year", "all" })
                                {
                                    using JsonDocument heat = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/heatmap?range=" + range + "&tzOffsetMinutes=-330"));
                                    JsonElement layout = heat.RootElement.GetProperty("Layout");
                                    int cells = layout.GetProperty("Rows").GetInt32() * layout.GetProperty("Columns").GetInt32();
                                    MuxAssert.AreEqual(range, layout.GetProperty("Range").GetString(), "heatmap range " + range);
                                    MuxAssert.AreEqual(330, layout.GetProperty("UtcOffsetMinutes").GetInt32(), "heatmap offset for " + range);
                                    MuxAssert.AreEqual(cells, heat.RootElement.GetProperty("TotalTokens").GetArrayLength(), "a token value per cell for " + range);
                                    MuxAssert.AreEqual(cells, heat.RootElement.GetProperty("Calls").GetArrayLength(), "a call count per cell for " + range);
                                    MuxAssert.AreEqual(layout.GetProperty("Rows").GetInt32(), layout.GetProperty("RowLabels").GetArrayLength(), "row labels for " + range);
                                    MuxAssert.AreEqual(layout.GetProperty("Columns").GetInt32(), layout.GetProperty("ColumnLabels").GetArrayLength(), "column labels for " + range);
                                    long heatCalls = 0, heatTokens = 0;
                                    foreach (JsonElement v in heat.RootElement.GetProperty("Calls").EnumerateArray()) heatCalls += v.GetInt64();
                                    foreach (JsonElement v in heat.RootElement.GetProperty("TotalTokens").EnumerateArray()) heatTokens += v.GetInt64();
                                    MuxAssert.AreEqual(3L, heatCalls, "every call in the " + range + " heatmap");
                                    MuxAssert.AreEqual(5100L, heatTokens, "every token in the " + range + " heatmap");
                                }

                                using (JsonDocument monthHeat = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/heatmap?range=month&tzOffsetMinutes=-330")))
                                {
                                    // Month: a 5-week calendar of half-days from a local Monday, covering the last 30 days.
                                    JsonElement layout = monthHeat.RootElement.GetProperty("Layout");
                                    MuxAssert.AreEqual(5, layout.GetProperty("Rows").GetInt32(), "month week rows");
                                    MuxAssert.AreEqual(14, layout.GetProperty("Columns").GetInt32(), "month half-day columns");
                                    MuxAssert.AreEqual(12L * 3_600_000L, layout.GetProperty("BucketMs").GetInt64(), "month half-day cells");
                                    MuxAssert.AreEqual("halfday", layout.GetProperty("ColumnKind").GetString(), "month column kind");
                                    MuxAssert.AreEqual("1w", layout.GetProperty("RowUnit").GetString(), "month row unit");
                                    MuxAssert.AreEqual(2, layout.GetProperty("ColumnGroup").GetInt32(), "two columns per weekday");
                                    MuxAssert.AreEqual("Mon", layout.GetProperty("ColumnLabels")[0].GetString(), "Monday AM");
                                    MuxAssert.AreEqual("Mon", layout.GetProperty("ColumnLabels")[1].GetString(), "Monday PM");
                                    MuxAssert.AreEqual("Sun", layout.GetProperty("ColumnLabels")[13].GetString(), "Sunday PM");
                                    MuxAssert.AreEqual("Rows: week starting · Columns: day (AM, PM)", layout.GetProperty("Caption").GetString(), "month caption");
                                    long from = layout.GetProperty("FromUnixMs").GetInt64();
                                    long windowFrom = layout.GetProperty("WindowFromUnixMs").GetInt64();
                                    long nowMs = layout.GetProperty("NowUnixMs").GetInt64();
                                    MuxAssert.IsTrue(windowFrom >= from && nowMs - windowFrom <= 30L * 86_400_000L, "the window is the last 30 days");
                                    DateTime localMonday = DateTimeOffset.FromUnixTimeMilliseconds(from + 330L * 60_000L).UtcDateTime;
                                    MuxAssert.AreEqual(DayOfWeek.Monday, localMonday.DayOfWeek, "rows start on a local Monday at UTC+5:30");
                                    MuxAssert.AreEqual(TimeSpan.Zero, localMonday.TimeOfDay, "rows start at local midnight at UTC+5:30");
                                }

                                using (JsonDocument quarterHeat = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/heatmap?range=quarter&model=gpt-4o")))
                                {
                                    JsonElement layout = quarterHeat.RootElement.GetProperty("Layout");
                                    MuxAssert.AreEqual(13, layout.GetProperty("Rows").GetInt32(), "quarter week rows");
                                    MuxAssert.AreEqual("Mon", layout.GetProperty("ColumnLabels")[0].GetString(), "quarter weekday columns");
                                    long calls = 0;
                                    foreach (JsonElement v in quarterHeat.RootElement.GetProperty("Calls").EnumerateArray()) calls += v.GetInt64();
                                    MuxAssert.AreEqual(2L, calls, "model filter applies to the heatmap");
                                }

                                // Split by model: one series per model on the total series' buckets.
                                using JsonDocument split = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/timeseries/split?range=day&dimension=model&top=6"));
                                MuxAssert.AreEqual(2, split.RootElement.GetProperty("Count").GetInt32(), "one series per model");
                                MuxAssert.AreEqual("gpt-4o", split.RootElement.GetProperty("Items")[0].GetProperty("Key").GetString(), "ranked by tokens");
                                MuxAssert.AreEqual(96, split.RootElement.GetProperty("Items")[0].GetProperty("Buckets").GetArrayLength(), "day buckets");

                                // An unknown split dimension is a 400.
                                HttpResponseMessage bad = http.GetAsync(baseUrl + "/v1.0/api/usage/timeseries/split?range=day&dimension=label").GetAwaiter().GetResult();
                                MuxAssert.AreEqual(400, (int)bad.StatusCode, "label split refused");

                                // Failures by error code.
                                using JsonDocument errorsDoc = JsonDocument.Parse(GetWithRetry(http, baseUrl + "/v1.0/api/usage/errors?range=all"));
                                MuxAssert.AreEqual(1, errorsDoc.RootElement.GetProperty("Count").GetInt32(), "one failure reason");
                                MuxAssert.AreEqual("http_429", errorsDoc.RootElement.GetProperty("Items")[0].GetProperty("ErrorCode").GetString(), "error code");
                                MuxAssert.AreEqual(1L, errorsDoc.RootElement.GetProperty("Items")[0].GetProperty("Count").GetInt64(), "error count");
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
