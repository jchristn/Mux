namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Core.Sessions;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="UsageChartQueries"/>, the TUI's in-process usage chart providers: the static
    /// shaping helpers (bucket labels per range, KPI line, failure-reason order, dimension rows), and the providers over a
    /// real SQLite store, checking that the <c>/usage</c> label filter applies to the time series, failure reasons, the
    /// dimension split and breakdown, the heatmap, and the bucket drill-down, and that "all time" starts at the oldest event.
    /// </summary>
    public static class UsageChartQueriesSuite
    {
        private const string SuiteId = "UsageChartQueries";
        private const long MinuteMs = 60_000L;
        private const long DayMs = 1440L * MinuteMs;

        /// <summary>
        /// Builds the usage-chart-queries suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the usage-chart-queries cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Usage chart queries: shaping helpers and filtered in-process providers",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "BuildChartDataShapesBuckets", "Shaping keeps the window, labels each bucket, sorts failure reasons, and fills the heatmap", (CancellationToken ct) =>
                    {
                        long now = 1_791_870_000_000L;
                        UsageWindow window = UsageWindow.Compute(UsageRange.Quarter, now);
                        List<UsageBucket> series = new List<UsageBucket>();
                        for (int i = 0; i < window.BucketCount; i++)
                        {
                            series.Add(new UsageBucket { BucketStartUnixMs = window.FromUnixMs + i * window.BucketMs, Metrics = new UsageMetrics { Calls = i, TotalTokens = i * 10 } });
                        }

                        List<UsageErrorCount> errors = new List<UsageErrorCount>
                        {
                            new UsageErrorCount { ErrorCode = "timeout", Count = 1 },
                            new UsageErrorCount { ErrorCode = "rate_limited", Count = 5 },
                            new UsageErrorCount { ErrorCode = "zero", Count = 0 }
                        };
                        UsageHeatmap heat = new UsageHeatmap(UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 0));
                        heat.TotalTokens[90] = 900;
                        UsageMetrics totals = new UsageMetrics { TotalTokens = 12_500, CostUsd = 1.5, Calls = 40, Errors = 2 };
                        UsageChartData data = UsageChartQueries.BuildChartData(UsageRange.Quarter, window, totals, "label:wip", series, errors, heat);

                        MuxAssert.AreEqual(window.FromUnixMs, data.WindowFromUnixMs, "window from");
                        MuxAssert.AreEqual(window.ToUnixMs, data.WindowToUnixMs, "window to");
                        MuxAssert.AreEqual(DayMs, data.BucketMs, "quarter buckets are a day");
                        MuxAssert.AreEqual(91, data.Buckets.Count, "every bucket kept");
                        MuxAssert.AreEqual(91, data.BucketLabels.Count, "every bucket labeled");
                        MuxAssert.AreEqual(UsageChartQueries.FormatBucketLabel(window.FromUnixMs, UsageRange.Quarter, DayMs), data.BucketLabels[0], "label format");
                        MuxAssert.AreEqual("label:wip — 12.5k tok · $1.50 · 40 calls · 2 err (5%)", data.HeaderLines[0], "header with scope and error rate");
                        MuxAssert.AreEqual(2, data.ErrorCounts.Count, "zero counts dropped");
                        MuxAssert.AreEqual("rate_limited", data.ErrorCounts[0].ErrorCode, "most frequent first");
                        MuxAssert.IsTrue(ReferenceEquals(heat, data.Heatmap), "heatmap kept");
                        MuxAssert.AreEqual(91, data.Heatmap!.Layout.CellCount, "heatmap cells");

                        UsageChartData empty = UsageChartQueries.BuildChartData(UsageRange.Day, UsageWindow.Compute(UsageRange.Day, now), null, null, null, null, null);
                        MuxAssert.AreEqual(0, empty.Buckets.Count, "no buckets");
                        MuxAssert.AreEqual("0 tok · $0.00 · 0 calls · 0 err (0%)", empty.HeaderLines[0], "empty KPI line");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "BucketLabelsPerRange", "Bucket labels use times for short ranges and dates for quarter, year, and all time", (CancellationToken ct) =>
                    {
                        long t = new DateTimeOffset(2026, 3, 9, 14, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        DateTimeOffset local = DateTimeOffset.FromUnixTimeMilliseconds(t).ToLocalTime();
                        string F(string format) => local.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
                        MuxAssert.AreEqual(F("HH:mm"), UsageChartQueries.FormatBucketLabel(t, UsageRange.Hour, MinuteMs), "hour");
                        MuxAssert.AreEqual(F("HH:mm"), UsageChartQueries.FormatBucketLabel(t, UsageRange.Day, 15 * MinuteMs), "day");
                        MuxAssert.AreEqual(F("ddd HH:mm"), UsageChartQueries.FormatBucketLabel(t, UsageRange.Week, 120 * MinuteMs), "week");
                        MuxAssert.AreEqual(F("MM/dd"), UsageChartQueries.FormatBucketLabel(t, UsageRange.Month, 720 * MinuteMs), "month");
                        MuxAssert.AreEqual(F("MM/dd"), UsageChartQueries.FormatBucketLabel(t, UsageRange.Quarter, DayMs), "quarter");
                        MuxAssert.AreEqual(F("yyyy-MM-dd"), UsageChartQueries.FormatBucketLabel(t, UsageRange.Year, 7 * DayMs), "year");
                        MuxAssert.AreEqual(F("yyyy-MM-dd"), UsageChartQueries.FormatBucketLabel(t, UsageRange.All, DayMs), "all, daily");
                        MuxAssert.AreEqual(F("MM/dd HH:mm"), UsageChartQueries.FormatBucketLabel(t, UsageRange.All, 6 * 60 * MinuteMs), "all, sub-day");
                        MuxAssert.AreEqual("1 week", UsageChartFormat.Interval(7 * DayMs), "week interval");
                        MuxAssert.AreEqual("+1h30", UsageChartFormat.Offset(90 * MinuteMs), "offset");
                        MuxAssert.AreEqual("+2d", UsageChartFormat.Offset(2 * DayMs), "day offset");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "BuildDimensionDataOrdersAndCaps", "Dimension shaping keeps Other last and caps rows at ten, sorted by cost then tokens", (CancellationToken ct) =>
                    {
                        List<UsageSeries> series = new List<UsageSeries>
                        {
                            new UsageSeries { Key = "Other", IsOther = true },
                            new UsageSeries { Key = "a" },
                            new UsageSeries { Key = "b" }
                        };
                        List<UsageBreakdownRow> rows = new List<UsageBreakdownRow>();
                        for (int i = 0; i < 12; i++)
                        {
                            rows.Add(new UsageBreakdownRow { Value = "v" + i, Metrics = new UsageMetrics { CostUsd = i % 3, TotalTokens = i } });
                        }

                        UsageDimensionData data = UsageChartQueries.BuildDimensionData("endpoint", series, rows);
                        MuxAssert.AreEqual("endpoint", data.Dimension, "dimension");
                        MuxAssert.AreEqual("a", data.Series[0].Key, "ranked series first");
                        MuxAssert.IsTrue(data.Series[2].IsOther, "Other last");
                        MuxAssert.AreEqual(10, data.Rows.Count, "at most ten rows");
                        MuxAssert.AreEqual("v11", data.Rows[0].Value, "highest cost, then most tokens");
                        MuxAssert.AreEqual("v8", data.Rows[1].Value, "tie on cost broken by tokens");
                        MuxAssert.AreEqual(0, UsageChartQueries.BuildDimensionData(string.Empty, null, null).Rows.Count, "null inputs");
                        MuxAssert.AreEqual("model", UsageChartQueries.BuildDimensionData(string.Empty, null, null).Dimension, "blank dimension defaults to model");
                        MuxAssert.AreEqual(12, data.AllRows.Count, "every row kept for ranking and export");
                        MuxAssert.AreEqual("v11", data.AllRows[0].Value, "all rows share the cost order");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "RankRowsByMetric", "Usage-by rows rank by tokens, calls, or cost, ties by tokens then cost then value, capped", (CancellationToken ct) =>
                    {
                        List<UsageBreakdownRow> rows = new List<UsageBreakdownRow>
                        {
                            new UsageBreakdownRow { Value = "cheap-chatty", Metrics = new UsageMetrics { TotalTokens = 100, Calls = 50, CostUsd = 0.01 } },
                            new UsageBreakdownRow { Value = "big", Metrics = new UsageMetrics { TotalTokens = 9000, Calls = 5, CostUsd = 0.5 } },
                            new UsageBreakdownRow { Value = "pricey", Metrics = new UsageMetrics { TotalTokens = 800, Calls = 5, CostUsd = 2.0 } },
                            new UsageBreakdownRow { Value = "b-tie", Metrics = new UsageMetrics { TotalTokens = 10, Calls = 1 } },
                            new UsageBreakdownRow { Value = "a-tie", Metrics = new UsageMetrics { TotalTokens = 10, Calls = 1 } }
                        };
                        string Order(string metric, int max = 0) => string.Join(",", UsageChartQueries.RankRows(rows, metric, max).Select(r => r.Value));
                        MuxAssert.AreEqual("big,pricey,cheap-chatty,a-tie,b-tie", Order("tokens"), "tokens");
                        MuxAssert.AreEqual("cheap-chatty,big,pricey,a-tie,b-tie", Order("calls"), "calls, tie by tokens");
                        MuxAssert.AreEqual("pricey,big,cheap-chatty,a-tie,b-tie", Order("cost"), "cost");
                        MuxAssert.AreEqual("big,pricey", Order("unknown", 2), "unknown metric is tokens; capped");
                        MuxAssert.AreEqual(0, UsageChartQueries.RankRows(null, "calls").Count, "null rows");
                        return Task.CompletedTask;
                    }),

                    StoreCase("LabelFilterAppliesToEveryQuery", "The label filter scopes the series, failure reasons, heatmap, split, breakdown, and drill-down", async (store, now, ct) =>
                    {
                        UsageQueryService query = NewQuery(store);
                        UsageChartQueries scoped = new UsageChartQueries(query, new List<string> { "wip" }, null, ct, () => now);
                        UsageChartData data = scoped.Load(UsageRange.Day);
                        MuxAssert.AreEqual(96, data.Buckets.Count, "day buckets");
                        MuxAssert.AreEqual(15 * MinuteMs, data.BucketMs, "day bucket width");
                        MuxAssert.AreEqual(3L, data.Buckets.Sum(b => b.Calls), "only wip calls in the series");
                        MuxAssert.AreEqual(1L, data.Buckets.Sum(b => b.Errors), "only wip errors");
                        MuxAssert.Contains("label:wip", data.HeaderLines[0], "header names the scope");
                        MuxAssert.Contains("3 calls", data.HeaderLines[0], "scoped KPI");
                        MuxAssert.AreEqual(1, data.ErrorCounts.Count, "only the wip failure reason");
                        MuxAssert.AreEqual("wip_error", data.ErrorCounts[0].ErrorCode, "wip reason");
                        MuxAssert.AreEqual(450L, data.Heatmap!.TotalTokens.Sum(), "heatmap scoped");
                        MuxAssert.AreEqual(3L, data.Heatmap.Calls.Sum(), "heatmap calls scoped");
                        MuxAssert.AreEqual(24, data.Heatmap.Layout.Rows, "day heatmap has hour rows");
                        MuxAssert.IsTrue(data.Buckets.Any(b => b.TotalMsDist.Count > 0), "distributions carried for box plots");

                        UsageDimensionData models = scoped.LoadDimension(data, "model");
                        MuxAssert.AreEqual(2, models.Series.Count, "two wip models");
                        MuxAssert.AreEqual(96, models.Series[0].Buckets.Count, "split aligned with the series");
                        MuxAssert.AreEqual(2, models.Rows.Count, "breakdown scoped");
                        MuxAssert.IsFalse(models.Rows.Any(r => r.Value == "prod-model"), "other sessions excluded");
                        UsageDimensionData endpoints = scoped.LoadDimension(data, "endpoint");
                        MuxAssert.AreEqual("endpoint", endpoints.Dimension, "endpoint dimension");

                        int bucket = data.Buckets.FindIndex(b => b.Calls == 2);
                        MuxAssert.IsTrue(bucket >= 0, "bucket with two wip calls");
                        long start = data.BucketStartsUnixMs[bucket];
                        UsageEventPage calls = scoped.LoadCalls(start, start + data.BucketMs - 1);
                        MuxAssert.AreEqual(2, calls.Items.Count, "drill-down scoped to the bucket and label");
                        MuxAssert.IsTrue(calls.Items.All(c => c.TimestampUnixMs >= start && c.TimestampUnixMs < start + data.BucketMs), "calls inside the bucket");

                        UsageChartQueries all = new UsageChartQueries(query, null, null, ct, () => now);
                        UsageChartData everything = all.Load(UsageRange.Day);
                        MuxAssert.AreEqual(5L, everything.Buckets.Sum(b => b.Calls), "unfiltered sees every call in the day");
                        MuxAssert.AreEqual(3, all.LoadCalls(start, start + data.BucketMs - 1).Items.Count, "unfiltered drill-down includes the prod call");
                        await Task.CompletedTask.ConfigureAwait(false);
                    }),

                    StoreCase("AllRangeStartsAtOldestEvent", "All time spans from the oldest matching event, with a matching heatmap", async (store, now, ct) =>
                    {
                        UsageQueryService query = NewQuery(store);
                        UsageChartData data = new UsageChartQueries(query, null, null, ct, () => now).Load(UsageRange.All);
                        long oldest = now - 40L * DayMs;
                        MuxAssert.IsTrue(data.WindowFromUnixMs <= oldest && data.WindowFromUnixMs > oldest - data.BucketMs, "window starts at the oldest event's bucket");
                        MuxAssert.IsTrue(data.Buckets.Count >= 60 && data.Buckets.Count <= 100, "about ninety buckets");
                        MuxAssert.AreEqual(6L, data.Buckets.Sum(b => b.Calls), "every call counted");
                        MuxAssert.AreEqual("all", data.Heatmap!.Layout.Range, "all-time heatmap");
                        MuxAssert.AreEqual(data.BucketMs, data.Heatmap.Layout.BucketMs, "heatmap width matches the all-time grid");
                        MuxAssert.AreEqual(6L, data.Heatmap.Calls.Sum(), "every call in the heatmap");
                        MuxAssert.IsTrue(data.Heatmap.Layout.FromUnixMs <= oldest, "heatmap reaches the oldest event");

                        UsageChartData wip = new UsageChartQueries(query, new List<string> { "wip" }, null, ct, () => now).Load(UsageRange.All);
                        MuxAssert.IsTrue(wip.WindowFromUnixMs > oldest, "the label scopes the oldest-event lookup");
                        await Task.CompletedTask.ConfigureAwait(false);
                    })
                });
        }

        #region Helpers

        private static UsageQueryService NewQuery(SqliteUsageStore store)
        {
            List<SessionMetadataEntry> entries = new List<SessionMetadataEntry>
            {
                new SessionMetadataEntry { Id = "s-wip", Labels = new List<string> { "wip" }, Tags = new List<SessionTag>() },
                new SessionMetadataEntry { Id = "s-prod", Labels = new List<string>(), Tags = new List<SessionTag> { new SessionTag("env", "prod") } }
            };
            return new UsageQueryService(store, () => new PricingTable(), new FakeMetadataIndex(entries));
        }

        private sealed class FakeMetadataIndex : ISessionMetadataIndex
        {
            private readonly IReadOnlyList<SessionMetadataEntry> _Entries;

            public FakeMetadataIndex(IReadOnlyList<SessionMetadataEntry> entries)
            {
                _Entries = entries;
            }

            public Task<IReadOnlyList<SessionMetadataEntry>> SnapshotAsync(CancellationToken token)
            {
                return Task.FromResult(_Entries);
            }
        }

        private static UsageEvent MakeEvent(long ts, string sessionId, string model, bool success = true, string? errorCode = null)
        {
            return new UsageEvent
            {
                TimestampUnixMs = ts,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = sessionId == "s-wip" ? "wip-endpoint" : "prod-endpoint",
                AdapterType = "openai",
                Model = model,
                SessionId = sessionId,
                InputTokens = 100,
                CachedTokens = 20,
                OutputTokens = 50,
                TotalTokens = 150,
                TimeToFirstTokenMs = 200,
                StreamingMs = 800,
                TotalMs = 1000,
                TokensPerSecond = 50,
                Success = success,
                ErrorCode = errorCode
            };
        }

        // Events: three wip calls in the last day (two sharing a bucket, one failed), two prod calls in the last day
        // (one in the shared bucket, one failed), and one prod call 40 days ago.
        private static TestCaseDescriptor StoreCase(string id, string name, Func<SqliteUsageStore, long, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux_uschart_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    using SqliteUsageStore store = new SqliteUsageStore(Path.Combine(dir, "usage.db"), 3650, 5_000_000);
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    long shared = ((now - 3L * 60L * MinuteMs) / (15 * MinuteMs)) * (15 * MinuteMs) + MinuteMs;
                    await store.InsertBatchAsync(new List<UsageEvent>
                    {
                        MakeEvent(shared, "s-wip", "wip-model-a"),
                        MakeEvent(shared + MinuteMs, "s-wip", "wip-model-b"),
                        MakeEvent(now - 6L * 60L * MinuteMs, "s-wip", "wip-model-a", false, "wip_error"),
                        MakeEvent(shared + 2 * MinuteMs, "s-prod", "prod-model"),
                        MakeEvent(now - 8L * 60L * MinuteMs, "s-prod", "prod-model", false, "prod_error"),
                        MakeEvent(now - 40L * DayMs, "s-prod", "prod-model")
                    }, ct).ConfigureAwait(false);
                    await body(store, now, ct).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(dir)) Directory.Delete(dir, true);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            });
        }

        #endregion
    }
}
