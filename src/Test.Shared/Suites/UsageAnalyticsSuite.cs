namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Telemetry;
    using Mux.Desktop.Services;
    using Touchstone.Core;

    /// <summary>
    /// Integration suite for <see cref="UsageAnalyticsService"/> against a real SQLite telemetry store:
    /// records events through the recorder, flushes, and verifies the service's summary, event page,
    /// endpoint filter, and distinct-endpoint list resolve correctly through the range window.
    /// </summary>
    public static class UsageAnalyticsSuite
    {
        /// <summary>
        /// Builds the usage-analytics integration suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the usage-analytics cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "UsageAnalytics",
                "Usage analytics service (integration)",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("UsageAnalytics", "RecordsAndQueries", "Recorded events surface through summary, events, and filters", async (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux-usage-tests", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);

                        MuxSettings settings = new MuxSettings();
                        UsageTelemetry telemetry = UsageTelemetry.Create(settings, dir, null);
                        try
                        {
                            MuxAssert.IsTrue(telemetry.Enabled, "telemetry enabled");

                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
                            telemetry.Recorder.Record(NewEvent(now, "ep-a", "m1", 100, 50));
                            telemetry.Recorder.Record(NewEvent(now, "ep-a", "m1", 200, 60));
                            telemetry.Recorder.Record(NewEvent(now, "ep-b", "m2", 10, 5));
                            await telemetry.FlushAsync(ct);

                            UsageQueryService? query = telemetry.CreateQueryService(() => new PricingTable());
                            MuxAssert.IsNotNull(query, "query service");
                            UsageAnalyticsService service = new UsageAnalyticsService(query!, telemetry.Enabled);

                            UsageSummary all = await service.GetSummaryAsync(UsageRange.Hour, null, null, ct);
                            MuxAssert.AreEqual(3L, all.Metrics.Calls, "total calls");

                            UsageSummary epA = await service.GetSummaryAsync(UsageRange.Hour, "ep-a", null, ct);
                            MuxAssert.AreEqual(2L, epA.Metrics.Calls, "ep-a calls");

                            UsageEventPage page = await service.GetEventsAsync(UsageRange.Hour, null, null, null, 1, 50, ct);
                            MuxAssert.AreEqual(3, page.TotalCount, "event page total");

                            List<string> endpoints = await service.GetEndpointsAsync(ct);
                            MuxAssert.IsTrue(endpoints.Contains("ep-a"), "endpoints has ep-a");
                            MuxAssert.IsTrue(endpoints.Contains("ep-b"), "endpoints has ep-b");
                        }
                        finally
                        {
                            telemetry.Dispose();
                            Cleanup(dir);
                        }
                    }),

                    new TestCaseDescriptor("UsageAnalytics", "SessionFilter", "Session-id filter scopes metrics to one conversation", async (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux-usage-tests", Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(dir);

                        MuxSettings settings = new MuxSettings();
                        UsageTelemetry telemetry = UsageTelemetry.Create(settings, dir, null);
                        try
                        {
                            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000L;
                            UsageEvent a = NewEvent(now, "ep-a", "m1", 100, 50);
                            a.SessionId = "s1";
                            UsageEvent b = NewEvent(now, "ep-a", "m1", 200, 60);
                            b.SessionId = "s1";
                            UsageEvent c = NewEvent(now, "ep-b", "m2", 10, 5);
                            c.SessionId = "s2";
                            telemetry.Recorder.Record(a);
                            telemetry.Recorder.Record(b);
                            telemetry.Recorder.Record(c);
                            await telemetry.FlushAsync(ct);

                            UsageQueryService? query = telemetry.CreateQueryService(() => new PricingTable());
                            MuxAssert.IsNotNull(query, "query service");

                            UsageEventPage allEvents = await query!.GetEventsAsync(new UsageFilter(), 1, 50, ct);
                            MuxAssert.AreEqual(3, allEvents.TotalCount, "total events recorded");

                            UsageEventPage s1Events = await query.GetEventsAsync(new UsageFilter { SessionId = "s1" }, 1, 50, ct);
                            MuxAssert.AreEqual(2, s1Events.TotalCount, "s1 events");

                            UsageSummary s1 = await query.GetSummaryAsync(new UsageFilter { SessionId = "s1" }, ct);
                            MuxAssert.AreEqual(2L, s1.Metrics.Calls, "s1 calls");
                            MuxAssert.AreEqual(300L, s1.Metrics.InputTokens, "s1 input tokens");

                            UsageSummary s2 = await query.GetSummaryAsync(new UsageFilter { SessionId = "s2" }, ct);
                            MuxAssert.AreEqual(1L, s2.Metrics.Calls, "s2 calls");
                        }
                        finally
                        {
                            telemetry.Dispose();
                            Cleanup(dir);
                        }
                    })
                });
        }

        private static UsageEvent NewEvent(long timestampUnixMs, string endpoint, string model, int inputTokens, int outputTokens)
        {
            return new UsageEvent
            {
                TimestampUnixMs = timestampUnixMs,
                EndpointName = endpoint,
                AdapterType = "test",
                Model = model,
                // Every recorded event must carry a conversation id or the recorder drops it.
                SessionId = "test-session",
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                TotalMs = 1200,
                TimeToFirstTokenMs = 300,
                Success = true
            };
        }

        private static void Cleanup(string dir)
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
                // Best-effort cleanup.
            }
        }
    }
}
