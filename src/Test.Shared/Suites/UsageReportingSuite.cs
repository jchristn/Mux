namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the shared reporting queries behind the usage charts on every surface: the quarter, year,
    /// and all-time ranges, range keys, series split by a dimension with "Other" folding, failure counts by error code,
    /// and the oldest-event lookup that sizes the all-time window.
    /// </summary>
    public static class UsageReportingSuite
    {
        private const string SuiteId = "UsageReporting";
        private const long MinuteMs = 60_000L;
        private const long DayMs = 1440L * MinuteMs;

        /// <summary>
        /// Builds the usage-reporting suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Usage reporting: long ranges, split series, failure reasons, and the all-time window",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "QuarterAndYearBuckets", "Quarter is 91 x 1-day and year is 52 x 1-week", (CancellationToken ct) =>
                    {
                        UsageWindow quarter = UsageWindow.Compute(UsageRange.Quarter, 1_000_000_000_000L);
                        MuxAssert.AreEqual(DayMs, quarter.BucketMs, "quarter bucket");
                        MuxAssert.AreEqual(91, quarter.BucketCount, "quarter count");
                        MuxAssert.AreEqual(91L * DayMs, quarter.ToUnixMs - quarter.FromUnixMs, "quarter span");
                        UsageWindow year = UsageWindow.Compute(UsageRange.Year, 1_000_000_000_000L);
                        MuxAssert.AreEqual(7L * DayMs, year.BucketMs, "year bucket");
                        MuxAssert.AreEqual(52, year.BucketCount, "year count");
                        MuxAssert.AreEqual(91L * DayMs, UsageWindow.NaturalSpanMs("quarter"), "quarter natural span");
                        MuxAssert.AreEqual(364L * DayMs, UsageWindow.NaturalSpanMs("YEAR"), "year natural span, case-insensitive");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "AllSpansFromOldestEvent", "All time spans the oldest event to now in about 90 nice buckets, and falls back to the day window with no events", (CancellationToken ct) =>
                    {
                        long now = 1_700_000_000_000L;
                        long earliest = now - (200L * DayMs);
                        UsageWindow all = UsageWindow.Compute(UsageRange.All, now, earliest);
                        MuxAssert.AreEqual(7L * DayMs, all.BucketMs, "200 days use weekly buckets");
                        MuxAssert.IsTrue(all.FromUnixMs <= earliest, "covers the oldest event");
                        MuxAssert.IsTrue(all.ToUnixMs > now, "covers now");
                        MuxAssert.AreEqual(0L, all.FromUnixMs % all.BucketMs, "start on the grid");
                        MuxAssert.AreEqual((int)((all.ToUnixMs - all.FromUnixMs) / all.BucketMs), all.BucketCount, "count matches span");
                        MuxAssert.IsTrue(all.BucketCount <= 92, "about ninety buckets at most");

                        UsageWindow hours = UsageWindow.Compute(UsageRange.All, now, now - (3L * 60L * MinuteMs));
                        MuxAssert.AreEqual(5L * MinuteMs, hours.BucketMs, "three hours use five-minute buckets");

                        UsageWindow empty = UsageWindow.Compute(UsageRange.All, now, 0);
                        UsageWindow day = UsageWindow.Compute(UsageRange.Day, now);
                        MuxAssert.AreEqual(day.BucketMs, empty.BucketMs, "no events falls back to the day grid");
                        MuxAssert.AreEqual(day.FromUnixMs, empty.FromUnixMs, "no events falls back to the day window");
                        MuxAssert.AreEqual(day.BucketMs, UsageWindow.Compute(UsageRange.All, now, now + 1).BucketMs, "an event in the future is ignored");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NiceBucketsReachAWeek", "Nice bucket widths step up to one week", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(DayMs, UsageWindow.NiceBucketMs(DayMs), "exactly a day");
                        MuxAssert.AreEqual(7L * DayMs, UsageWindow.NiceBucketMs(DayMs + 1), "just over a day");
                        MuxAssert.AreEqual(7L * DayMs, UsageWindow.NiceBucketMs(100L * DayMs), "capped at a week");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "RangeKeysRoundTrip", "Every range has a key that parses back, and unknown keys use the fallback", (CancellationToken ct) =>
                    {
                        foreach (UsageRange range in Enum.GetValues<UsageRange>())
                        {
                            MuxAssert.AreEqual(range, UsageWindow.ParseRange(UsageWindow.KeyOf(range), UsageRange.Hour == range ? UsageRange.Day : UsageRange.Hour), "round trip " + range);
                        }

                        MuxAssert.AreEqual(UsageRange.Quarter, UsageWindow.ParseRange("  Quarter ", UsageRange.Day), "trimmed and case-insensitive");
                        MuxAssert.AreEqual(UsageRange.Week, UsageWindow.ParseRange("decade", UsageRange.Week), "unknown key");
                        MuxAssert.AreEqual(UsageRange.Day, UsageWindow.ParseRange(null, UsageRange.Day), "null key");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapLongRangesAlignToDays", "Month, quarter and year heatmaps align to days; all time reaches back to the oldest event", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        foreach (UsageRange range in new[] { UsageRange.Month, UsageRange.Quarter, UsageRange.Year })
                        {
                            UsageHeatmapLayout heat = UsageHeatmapLayout.Compute(range, now, 0);
                            MuxAssert.AreEqual(0L, heat.FromUnixMs % DayMs, "start on a day for " + range);
                            MuxAssert.IsTrue(heat.FromUnixMs <= now && now < heat.ToUnixMs, "now included for " + range);
                        }

                        UsageHeatmapLayout year = UsageHeatmapLayout.Compute(UsageRange.Year, now, 0);
                        MuxAssert.AreEqual(DayMs, year.BucketMs, "the year heatmap has daily cells");
                        MuxAssert.AreEqual(now / DayMs * DayMs - 363L * DayMs, year.WindowFromUnixMs, "the year heatmap covers the last 364 days");

                        long earliest = now - (30L * DayMs);
                        UsageHeatmapLayout allHeat = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, earliest);
                        MuxAssert.IsTrue(allHeat.IndexOf(earliest) >= 0, "all time holds the oldest event");
                        MuxAssert.IsTrue(allHeat.IndexOf(now) >= 0, "all time holds now");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapScaleUsesQuartiles", "Heatmap shades split the used cells into quartiles, so a skewed peak does not flatten everything into the faintest shade", (CancellationToken ct) =>
                    {
                        // One huge bucket and a spread of small ones: a linear scale would put every small one at level 1.
                        List<double> values = new List<double> { 0, 0, 10, 20, 30, 40, 50, 60, 70, 80, 10_000 };
                        UsageHeatmapScale scale = UsageHeatmapScale.FromValues(values);
                        MuxAssert.AreEqual(3, scale.UpperBounds.Count, "three bounds for four shades");
                        MuxAssert.AreEqual(10_000.0, scale.Peak, "peak");
                        MuxAssert.AreEqual(0, scale.LevelOf(0), "no usage");
                        int[] counts = new int[UsageHeatmapScale.Levels + 1];
                        foreach (double v in values)
                        {
                            counts[scale.LevelOf(v)]++;
                        }

                        MuxAssert.AreEqual(2, counts[0], "two empty cells");
                        for (int level = 1; level <= UsageHeatmapScale.Levels; level++)
                        {
                            MuxAssert.IsTrue(counts[level] >= 2 && counts[level] <= 3, "level " + level + " holds about a quarter of the nine used cells");
                        }

                        MuxAssert.AreEqual(UsageHeatmapScale.Levels, scale.LevelOf(10_000), "the peak is the strongest shade");
                        MuxAssert.IsTrue(scale.LevelOf(10) < scale.LevelOf(80), "monotone");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapScaleEdgeCases", "Equal values take the strongest shade, empty or invalid input has no shades, and non-finite values are ignored", (CancellationToken ct) =>
                    {
                        UsageHeatmapScale equal = UsageHeatmapScale.FromValues(new double[] { 5, 5, 5 });
                        MuxAssert.AreEqual(UsageHeatmapScale.Levels, equal.LevelOf(5), "all equal: strongest shade");
                        UsageHeatmapScale single = UsageHeatmapScale.FromValues(new double[] { 0, 42 });
                        MuxAssert.AreEqual(UsageHeatmapScale.Levels, single.LevelOf(42), "one used cell: strongest shade");
                        UsageHeatmapScale none = UsageHeatmapScale.FromValues(null);
                        MuxAssert.AreEqual(0, none.UpperBounds.Count, "no bounds");
                        MuxAssert.AreEqual(0.0, none.Peak, "no peak");
                        MuxAssert.AreEqual(0, none.LevelOf(0), "zero is no usage");
                        UsageHeatmapScale noisy = UsageHeatmapScale.FromValues(new[] { double.NaN, double.PositiveInfinity, -3, 7 });
                        MuxAssert.AreEqual(7.0, noisy.Peak, "non-finite and negative values ignored");
                        MuxAssert.AreEqual(0, noisy.LevelOf(double.NaN), "NaN is no usage");
                        MuxAssert.AreEqual(0, noisy.LevelOf(-1), "negative is no usage");
                        return Task.CompletedTask;
                    }),

                    StoreCase("SplitKeepsTopAndFoldsOther", "A split keeps the top values by tokens, folds the rest into Other, and stacks to the total", async (UsageQueryService query, SqliteUsageStore store, CancellationToken ct) =>
                    {
                        List<UsageEvent> events = new List<UsageEvent>();
                        string[] models = { "m1", "m2", "m3", "m4", "m5" };
                        for (int i = 0; i < models.Length; i++)
                        {
                            // m1 has the most tokens, m5 the fewest.
                            events.Add(MakeEvent(1100 + i, models[i], "ep-" + (i % 2), (models.Length - i) * 100));
                        }

                        events.Add(MakeEvent(2500, "m1", "ep-0", 1000));
                        await store.InsertBatchAsync(events, ct);
                        UsageFilter filter = new UsageFilter { FromUnixMs = 1000, ToUnixMs = 3999 };
                        List<UsageSeries> split = await query.GetSplitTimeseriesAsync("model", filter, 1000, 2, ct);
                        List<UsageBucket> totals = await query.GetTimeseriesAsync(filter, 1000, ct);
                        MuxAssert.AreEqual(3, split.Count, "two kept plus Other");
                        MuxAssert.AreEqual("m1", split[0].Key, "largest first");
                        MuxAssert.AreEqual("m2", split[1].Key, "second largest");
                        MuxAssert.IsTrue(split[2].IsOther, "remainder folded");
                        MuxAssert.AreEqual("Other", split[2].Key, "Other key");
                        MuxAssert.AreEqual(3L, split[2].Buckets[0].Metrics.Calls, "three models folded");
                        for (int b = 0; b < totals.Count; b++)
                        {
                            MuxAssert.AreEqual(totals[b].BucketStartUnixMs, split[0].Buckets[b].BucketStartUnixMs, "aligned buckets");
                            MuxAssert.AreEqual(totals[b].Metrics.TotalTokens, split.Sum(s => s.Buckets[b].Metrics.TotalTokens), "series stack to the total");
                            MuxAssert.AreEqual(totals[b].Metrics.Calls, split.Sum(s => s.Buckets[b].Metrics.Calls), "calls stack to the total");
                        }

                        List<UsageSeries> endpoints = await query.GetSplitTimeseriesAsync("endpoint", filter, 1000, 6, ct);
                        MuxAssert.AreEqual(2, endpoints.Count, "two endpoints, nothing folded");
                        MuxAssert.IsFalse(endpoints.Any(s => s.IsOther), "no Other when everything fits");

                        List<UsageSeries> single = await query.GetSplitTimeseriesAsync("model", filter, 1000, 4, ct);
                        MuxAssert.AreEqual(5, single.Count, "one value past the top is shown, not folded");
                        MuxAssert.IsFalse(single.Any(s => s.IsOther), "an Other of one value would hide its name");
                    }),

                    StoreCase("SplitDimensions", "Every split dimension is accepted, aliases work, and labels, tags, and unknown names are refused", async (UsageQueryService query, SqliteUsageStore store, CancellationToken ct) =>
                    {
                        await store.InsertBatchAsync(new List<UsageEvent> { MakeEvent(1100, "m1", "ep-0", 100) }, ct);
                        UsageFilter filter = new UsageFilter { FromUnixMs = 1000, ToUnixMs = 1999 };
                        foreach (string dim in UsageQueryService.SplitDimensions.Concat(new[] { "adapter", "call_kind", "MODEL" }))
                        {
                            List<UsageSeries> series = await query.GetSplitTimeseriesAsync(dim, filter, 1000, 6, ct);
                            MuxAssert.AreEqual(1, series.Count, "one value for " + dim);
                        }

                        MuxAssert.AreEqual("ep-0", (await query.GetSplitTimeseriesAsync("endpoint", filter, 1000, 6, ct))[0].Key, "endpoint key");
                        MuxAssert.AreEqual("primary", (await query.GetSplitTimeseriesAsync("callkind", filter, 1000, 6, ct))[0].Key, "call kind key");
                        foreach (string bad in new[] { "label", "tag", "planet" })
                        {
                            bool threw = false;
                            try { await query.GetSplitTimeseriesAsync(bad, filter, 1000, 6, ct); }
                            catch (ArgumentException) { threw = true; }
                            MuxAssert.IsTrue(threw, bad + " refused");
                        }
                    }),

                    StoreCase("ErrorCountsByCode", "Failures are counted by error code, most frequent first, with blank codes as unknown and successes excluded", async (UsageQueryService query, SqliteUsageStore store, CancellationToken ct) =>
                    {
                        List<UsageEvent> events = new List<UsageEvent>
                        {
                            MakeEvent(1100, "m1", "ep-0", 100),
                            Failed(MakeEvent(1200, "m1", "ep-0", 100), "http_429"),
                            Failed(MakeEvent(1300, "m1", "ep-1", 100), "http_429"),
                            Failed(MakeEvent(1400, "m2", "ep-0", 100), "timeout"),
                            Failed(MakeEvent(1500, "m2", "ep-0", 100), null),
                            Failed(MakeEvent(5000, "m2", "ep-0", 100), "late")
                        };
                        await store.InsertBatchAsync(events, ct);
                        List<UsageErrorCount> counts = await query.GetErrorCountsAsync(new UsageFilter { FromUnixMs = 1000, ToUnixMs = 1999 }, ct);
                        MuxAssert.AreEqual(3, counts.Count, "three reasons in the window");
                        MuxAssert.AreEqual("http_429", counts[0].ErrorCode, "most frequent first");
                        MuxAssert.AreEqual(2L, counts[0].Count, "two rate limits");
                        MuxAssert.IsTrue(counts.Any(c => c.ErrorCode == "unknown" && c.Count == 1), "blank code is unknown");
                        List<UsageErrorCount> endpoint = await query.GetErrorCountsAsync(new UsageFilter { FromUnixMs = 1000, ToUnixMs = 1999, EndpointName = "ep-1" }, ct);
                        MuxAssert.AreEqual(1, endpoint.Count, "filters apply");
                        MuxAssert.AreEqual(0, (await query.GetErrorCountsAsync(new UsageFilter { FromUnixMs = 1000, ToUnixMs = 1150 }, ct)).Count, "no failures, no rows");
                    }),

                    StoreCase("EarliestEventAndAllWindow", "The oldest-event lookup ignores the window but honors other filters, and sizes the all-time window", async (UsageQueryService query, SqliteUsageStore store, CancellationToken ct) =>
                    {
                        long now = 1_700_000_000_000L;
                        await store.InsertBatchAsync(new List<UsageEvent>
                        {
                            MakeEvent(now - (40L * DayMs), "m1", "ep-0", 100),
                            MakeEvent(now - (10L * DayMs), "m2", "ep-1", 100)
                        }, ct);
                        MuxAssert.AreEqual(0L, await query.GetEarliestEventUnixMsAsync(new UsageFilter { Model = "none" }, ct), "nothing matches");
                        MuxAssert.AreEqual(now - (40L * DayMs), await query.GetEarliestEventUnixMsAsync(new UsageFilter { FromUnixMs = now - DayMs, ToUnixMs = now }, ct), "window ignored");
                        MuxAssert.AreEqual(now - (10L * DayMs), await query.GetEarliestEventUnixMsAsync(new UsageFilter { EndpointName = "ep-1" }, ct), "endpoint honored");
                        MuxAssert.AreEqual(now - (40L * DayMs), await query.GetEarliestEventUnixMsAsync(null, ct), "null filter");

                        UsageWindow all = await query.ResolveWindowAsync(UsageRange.All, null, now, ct);
                        MuxAssert.IsTrue(all.FromUnixMs <= now - (40L * DayMs), "all time starts at the oldest event");
                        MuxAssert.AreEqual(720L * MinuteMs, all.BucketMs, "40 days use twelve-hour buckets");
                        UsageWindow week = await query.ResolveWindowAsync(UsageRange.Week, null, now, ct);
                        MuxAssert.AreEqual(UsageWindow.Compute(UsageRange.Week, now).FromUnixMs, week.FromUnixMs, "other ranges need no lookup");
                    })
                });
        }

        private static UsageEvent MakeEvent(long tsUtc, string model, string endpoint, int tokens)
        {
            return new UsageEvent
            {
                TimestampUnixMs = tsUtc,
                CallKind = UsageCallKindEnum.Primary,
                Command = "test",
                EndpointName = endpoint,
                AdapterType = "openai",
                Model = model,
                InputTokens = tokens,
                OutputTokens = 0,
                TotalTokens = tokens,
                TimeToFirstTokenMs = 40,
                StreamingMs = 200,
                TotalMs = 240,
                TokensPerSecond = 10,
                FinishReason = "stop",
                Success = true
            };
        }

        private static UsageEvent Failed(UsageEvent evt, string? code)
        {
            evt.Success = false;
            evt.ErrorCode = code;
            return evt;
        }

        private static TestCaseDescriptor StoreCase(string id, string name, Func<UsageQueryService, SqliteUsageStore, CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, async (CancellationToken ct) =>
            {
                string dir = Path.Combine(Path.GetTempPath(), "mux_usagerep_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                try
                {
                    using SqliteUsageStore store = new SqliteUsageStore(Path.Combine(dir, "usage.db"), 3650, 5_000_000);
                    UsageQueryService query = new UsageQueryService(store, () => new PricingTable());
                    await body(query, store, ct).ConfigureAwait(false);
                }
                finally
                {
                    try { Directory.Delete(dir, true); } catch (Exception) { }
                }
            });
        }
    }
}
