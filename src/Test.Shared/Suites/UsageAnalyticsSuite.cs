namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
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
    /// endpoint filter, and distinct-endpoint list resolve correctly through the range window; and, over a
    /// seeded store, that the desktop chart bundle matches the shared query service for every range, that
    /// endpoint/session scopes and drill-down windows filter exactly, and that nothing is capped client-side.
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
                    }),

                    StoreCase("DashboardMatchesQueryService", "The dashboard bundle matches the shared timeseries, split, errors, and breakdown queries, percentiles included", async (UsageQueryService query, SqliteUsageStore store, UsageAnalyticsService service, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(SeedEvents(), ct);
                        UsageDashboardData data = await service.LoadDashboardAsync(UsageRange.Day, null, "model", ct);

                        UsageWindow window = UsageWindow.Compute(UsageRange.Day, Now);
                        MuxAssert.AreEqual(window.FromUnixMs, data.Window.FromUnixMs, "day window");
                        MuxAssert.AreEqual(96, data.Timeseries.Count, "dense day grid");
                        UsageFilter filter = new UsageFilter { FromUnixMs = window.FromUnixMs, ToUnixMs = window.ToUnixMs - 1 };
                        List<UsageBucket> expected = await query.GetTimeseriesAsync(filter, window.BucketMs, ct);
                        MuxAssert.AreEqual(expected.Count, data.Timeseries.Count, "same bucket count as the query service");
                        for (int i = 0; i < expected.Count; i++)
                        {
                            MuxAssert.AreEqual(expected[i].Metrics.Calls, data.Timeseries[i].Metrics.Calls, "calls in bucket " + i);
                            MuxAssert.AreEqual(expected[i].Metrics.TotalMsDist.P95, data.Timeseries[i].Metrics.TotalMsDist.P95, "p95 in bucket " + i);
                        }

                        MuxAssert.AreEqual(6L, data.Summary.Metrics.Calls, "calls in the day (the 40-day-old call is outside)");
                        MuxAssert.AreEqual(2L, data.Summary.Metrics.Errors, "errors");
                        MuxAssert.IsTrue(data.Summary.Metrics.P50TotalMs > 0, "p50 comes from the query service");
                        MuxAssert.IsTrue(data.Timeseries.Exists(b => b.Metrics.TotalMsDist.P95 > 0), "bucket distributions are filled");
                        MuxAssert.AreEqual("timeout:1,unknown:1", string.Join(",", data.Errors.ConvertAll(e => e.ErrorCode + ":" + e.Count)), "failure reasons");
                        MuxAssert.AreEqual("model", data.Dimension, "dimension");
                        MuxAssert.AreEqual(2, data.Split.Count, "split by model: m1 and m2");
                        MuxAssert.AreEqual(2, data.Breakdown.Count, "breakdown by model");

                        UsageDashboardData byEndpoint = await service.LoadDashboardAsync(UsageRange.Day, null, "endpoint", ct);
                        MuxAssert.AreEqual(3, byEndpoint.Split.Count, "split by endpoint");
                        MuxAssert.AreEqual(3, byEndpoint.Breakdown.Count, "breakdown by endpoint");
                    }),

                    StoreCase("LongRanges", "Quarter, year, and all-time windows come from the shared grid, with all sized from the oldest call", async (UsageQueryService query, SqliteUsageStore store, UsageAnalyticsService service, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(SeedEvents(), ct);
                        UsageDashboardData quarter = await service.LoadDashboardAsync(UsageRange.Quarter, null, "model", ct);
                        MuxAssert.AreEqual(91, quarter.Timeseries.Count, "quarter buckets");
                        MuxAssert.AreEqual(7L, quarter.Summary.Metrics.Calls, "quarter includes the 40-day-old call");
                        UsageDashboardData year = await service.LoadDashboardAsync(UsageRange.Year, null, "model", ct);
                        MuxAssert.AreEqual(52, year.Timeseries.Count, "year buckets");

                        UsageDashboardData all = await service.LoadDashboardAsync(UsageRange.All, null, "model", ct);
                        MuxAssert.AreEqual(720L * 60_000L, all.Window.BucketMs, "40 days of history use twelve-hour buckets");
                        MuxAssert.IsTrue(all.Window.FromUnixMs <= Now - (40L * DayMs), "all starts at the oldest call");
                        MuxAssert.AreEqual(7L, all.Summary.Metrics.Calls, "all calls");
                        long sum = 0;
                        foreach (UsageBucket bucket in all.Timeseries)
                        {
                            sum += bucket.Metrics.Calls;
                        }

                        MuxAssert.AreEqual(7L, sum, "buckets add up");
                        MuxAssert.IsNotNull(all.Heatmap, "all-time heatmap loaded");
                        MuxAssert.AreEqual(7L, all.Heatmap!.Calls.Sum(), "every call in the all-time heatmap");
                        MuxAssert.AreEqual(7L, quarter.Heatmap!.Calls.Sum(), "every call in the quarter heatmap");
                        MuxAssert.AreEqual(13, quarter.Heatmap.Layout.Columns, "quarter heatmap has week columns");

                        UsageSummary legacy = await service.GetSummaryAsync(UsageRange.All, null, null, ct);
                        MuxAssert.AreEqual(7L, legacy.Metrics.Calls, "the range-based summary also honors all time");
                    }),

                    StoreCase("ScopeFilters", "Endpoint sets combine per endpoint, session sets scope exactly, and an unmatched filter is empty", async (UsageQueryService query, SqliteUsageStore store, UsageAnalyticsService service, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(SeedEvents(), ct);

                        UsageDashboardData one = await service.LoadDashboardAsync(UsageRange.Day, new UsageScope(new List<string> { "ep-a" }), "model", ct);
                        MuxAssert.AreEqual(3L, one.Summary.Metrics.Calls, "ep-a only");

                        UsageDashboardData two = await service.LoadDashboardAsync(UsageRange.Day, new UsageScope(new List<string> { "ep-a", "ep-b" }), "endpoint", ct);
                        MuxAssert.AreEqual(5L, two.Summary.Metrics.Calls, "ep-a and ep-b combined");
                        MuxAssert.AreEqual(2L, two.Summary.Metrics.Errors, "errors combined");
                        MuxAssert.AreEqual(96, two.Timeseries.Count, "combined series keeps the grid");
                        MuxAssert.AreEqual(2, two.Breakdown.Count, "breakdown has both endpoints");
                        MuxAssert.AreEqual(2, two.Split.Count, "split has both endpoints");
                        MuxAssert.AreEqual(2, two.Errors.Count, "failure reasons combined");

                        UsageDashboardData session = await service.LoadDashboardAsync(UsageRange.Day, new UsageScope(null, new[] { "s2" }), "model", ct);
                        MuxAssert.AreEqual(2L, session.Summary.Metrics.Calls, "session s2 only");

                        UsageDashboardData none = await service.LoadDashboardAsync(UsageRange.Day, new UsageScope(new List<string>()), "model", ct);
                        MuxAssert.AreEqual(0L, none.Summary.Metrics.Calls, "unmatched endpoint filter is empty");
                        MuxAssert.AreEqual(96, none.Timeseries.Count, "still a dense, empty grid");
                        UsageDashboardData noSession = await service.LoadDashboardAsync(UsageRange.All, new UsageScope(null, new string[0]), "model", ct);
                        MuxAssert.AreEqual(0L, noSession.Summary.Metrics.Calls, "unmatched conversation filter is empty");
                    }),

                    StoreCase("EventsInWindow", "The call table and drill-down read the calls of one window, newest first, across matched endpoints", async (UsageQueryService query, SqliteUsageStore store, UsageAnalyticsService service, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(SeedEvents(), ct);
                        UsageDashboardData data = await service.LoadDashboardAsync(UsageRange.Day, null, "model", ct);
                        UsageBucket busy = data.Timeseries.Find(b => b.Metrics.Calls == 3)!;
                        MuxAssert.IsNotNull(busy, "the bucket with three calls");
                        (long from, long to) = UsageChartShaping.DrillWindow(busy.BucketStartUnixMs, UsageChartShaping.BucketWidthMs(data.Timeseries, data.Window));
                        UsageEventPage bucket = await service.GetEventsInWindowAsync(from, to, null, 50, ct);
                        MuxAssert.AreEqual(3L, bucket.TotalCount, "drill-down total matches the bucket");
                        MuxAssert.AreEqual(3, bucket.Items.Count, "drill-down rows");

                        UsageEventPage whole = await service.GetEventsInWindowAsync(data.Window.FromUnixMs, data.Window.ToUnixMs - 1, new UsageScope(new List<string> { "ep-a", "ep-c" }), 2, ct);
                        MuxAssert.AreEqual(4L, whole.TotalCount, "ep-a and ep-c total");
                        MuxAssert.AreEqual(2, whole.Items.Count, "limited to the page size");
                        MuxAssert.IsTrue(whole.Items[0].TimestampUnixMs >= whole.Items[1].TimestampUnixMs, "newest first");
                    }),

                    StoreCase("NoTwentyThousandCap", "Charts and KPIs count every call, beyond the old 20,000-row client limit", async (UsageQueryService query, SqliteUsageStore store, UsageAnalyticsService service, CancellationToken ct) =>
                    {
                        List<UsageEvent> many = new List<UsageEvent>();
                        for (int i = 0; i < 20_100; i++)
                        {
                            many.Add(Seed(Now - 1000 - (i * 100L), "ep-a", "m1", "s1", 10, i % 100 == 0 ? "timeout" : null, true));
                        }

                        await store.InsertBatchAsync(many, ct);
                        UsageDashboardData data = await service.LoadDashboardAsync(UsageRange.Hour, null, "model", ct);
                        MuxAssert.AreEqual(20_100L, data.Summary.Metrics.Calls, "every call counted");
                        MuxAssert.AreEqual(201L, data.Summary.Metrics.Errors, "every failure counted");
                        long sum = 0;
                        foreach (UsageBucket bucket in data.Timeseries)
                        {
                            sum += bucket.Metrics.Calls;
                        }

                        MuxAssert.AreEqual(20_100L, sum, "chart buckets add up");
                    })
                });
        }

        private const long DayMs = 1440L * 60_000L;

        // A fixed "now" on a day-grid boundary plus a little, so seeded calls fall in predictable buckets.
        private static readonly long Now = (1_700_000_000_000L / (15L * 60_000L)) * (15L * 60_000L) + 5L * 60_000L;

        private static List<UsageEvent> SeedEvents()
        {
            return new List<UsageEvent>
            {
                // Three calls in one 15-minute bucket (one failed with no code).
                Seed(Now - 60_000L, "ep-a", "m1", "s1", 100, null, false),
                Seed(Now - 61_000L, "ep-a", "m1", "s1", 200, null, true),
                Seed(Now - 62_000L, "ep-b", "m2", "s2", 50, "timeout", true),
                // Three more spread across the day.
                Seed(Now - (3L * 3_600_000L), "ep-a", "m2", "s1", 40, null, true),
                Seed(Now - (5L * 3_600_000L), "ep-b", "m1", "s2", 30, null, true),
                Seed(Now - (7L * 3_600_000L), "ep-c", "m1", "s3", 20, null, true),
                // One old call, outside the day but inside the quarter.
                Seed(Now - (40L * DayMs), "ep-c", "m1", "s3", 10, null, true)
            };
        }

        private static UsageEvent Seed(long ts, string endpoint, string model, string session, int tokens, string? errorCode, bool successUnlessCode)
        {
            bool success = successUnlessCode && errorCode == null;
            return new UsageEvent
            {
                TimestampUnixMs = ts,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = endpoint,
                AdapterType = "openai",
                Model = model,
                SessionId = session,
                InputTokens = tokens,
                CachedTokens = tokens / 4,
                OutputTokens = tokens / 2,
                TotalTokens = tokens + (tokens / 2),
                TimeToFirstTokenMs = 40 + tokens,
                StreamingMs = 200,
                TotalMs = 240 + tokens,
                TokensPerSecond = 10,
                FinishReason = "stop",
                Success = success,
                ErrorCode = errorCode
            };
        }

        private static TestCaseDescriptor StoreCase(string id, string name, Func<UsageQueryService, SqliteUsageStore, UsageAnalyticsService, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("UsageAnalytics", id, name, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux-usage-tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    using SqliteUsageStore store = new SqliteUsageStore(Path.Combine(dir, "usage.db"), 3650, 5_000_000);
                    UsageQueryService query = new UsageQueryService(store, () => new PricingTable());
                    UsageAnalyticsService service = new UsageAnalyticsService(query, true, () => Now);
                    await body(query, store, service, ct).ConfigureAwait(false);
                }
                finally
                {
                    Cleanup(dir);
                }
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
