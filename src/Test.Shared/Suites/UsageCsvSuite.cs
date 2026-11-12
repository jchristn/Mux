namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="UsageCsv"/>, the one CSV format the usage chart exports share: number, time,
    /// and cell formatting, file names, and the exact text of every chart's export for a fixed fixture (the web and
    /// VS Code JavaScript is checked against the same expected text).
    /// </summary>
    public static class UsageCsvSuite
    {
        private const string SuiteId = "UsageCsv";
        private const long MinuteMs = 60_000L;

        // 2026-11-09T08:30:00Z, which is 14:00 at +05:30.
        private const long T0 = 1_794_213_000_000L;

        /// <summary>
        /// Builds the CSV suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Usage CSV export: formatting, file names, and per-chart text",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "Numbers", "Invariant numbers: integers plain, six decimals trimmed, no exponent, empty for NaN", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("0", UsageCsv.FormatNumber(0.0), "zero");
                        MuxAssert.AreEqual("0", UsageCsv.FormatNumber(-0.0), "negative zero");
                        MuxAssert.AreEqual("12", UsageCsv.FormatNumber(12.0), "integral double");
                        MuxAssert.AreEqual("1234567890123", UsageCsv.FormatNumber(1_234_567_890_123L), "long");
                        MuxAssert.AreEqual("1234567.5", UsageCsv.FormatNumber(1_234_567.5), "no thousands separator");
                        MuxAssert.AreEqual("0.123457", UsageCsv.FormatNumber(0.1234567), "six decimals, rounded");
                        MuxAssert.AreEqual("0.000001", UsageCsv.FormatNumber(0.000001), "no exponent");
                        MuxAssert.AreEqual("0", UsageCsv.FormatNumber(0.0000004), "below half a millionth");
                        MuxAssert.AreEqual("0.25", UsageCsv.FormatNumber(0.25), "trailing zeros trimmed");
                        MuxAssert.AreEqual("-1.5", UsageCsv.FormatNumber(-1.5), "negative");
                        MuxAssert.AreEqual(string.Empty, UsageCsv.FormatNumber(double.NaN), "NaN");
                        MuxAssert.AreEqual(string.Empty, UsageCsv.FormatNumber(double.PositiveInfinity), "infinity");

                        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
                        try
                        {
                            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                            MuxAssert.AreEqual("1234.5", UsageCsv.FormatNumber(1234.5), "culture-independent");
                        }
                        finally
                        {
                            System.Globalization.CultureInfo.CurrentCulture = saved;
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "TimesAndCells", "ISO local time with the offset of each instant; minimal quoting", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("2026-11-09T14:00:00+05:30", UsageCsv.FormatTime(T0, Fixed()), "fixed offset");
                        MuxAssert.AreEqual("2026-11-09T08:30:00+00:00", UsageCsv.FormatTime(T0, TimeZoneInfo.Utc), "UTC");
                        TimeZoneInfo? ny = NewYork();
                        if (ny != null)
                        {
                            // US daylight time began 2026-03-08 at 07:00Z.
                            MuxAssert.AreEqual("2026-03-08T01:30:00-05:00", UsageCsv.FormatTime(1_772_951_400_000L, ny), "standard time");
                            MuxAssert.AreEqual("2026-03-08T03:30:00-04:00", UsageCsv.FormatTime(1_772_955_000_000L, ny), "daylight time");
                        }

                        MuxAssert.AreEqual("gpt-4o", UsageCsv.Escape("gpt-4o"), "plain");
                        MuxAssert.AreEqual("\"a,b\"", UsageCsv.Escape("a,b"), "comma");
                        MuxAssert.AreEqual("\"say \"\"hi\"\"\"", UsageCsv.Escape("say \"hi\""), "quotes doubled");
                        MuxAssert.AreEqual("\"line\nbreak\"", UsageCsv.Escape("line\nbreak"), "newline");
                        MuxAssert.AreEqual("\" padded\"", UsageCsv.Escape(" padded"), "leading space");
                        MuxAssert.AreEqual(string.Empty, UsageCsv.Escape(null), "null");
                        MuxAssert.AreEqual("'=HYPERLINK(1)", UsageCsv.Escape("=HYPERLINK(1)"), "formula guarded");
                        MuxAssert.AreEqual("'+cmd", UsageCsv.Escape("+cmd"), "plus guarded");
                        MuxAssert.AreEqual("'@sum", UsageCsv.Escape("@sum"), "at guarded");
                        MuxAssert.AreEqual("'-x", UsageCsv.Escape("-x"), "minus text guarded");
                        MuxAssert.AreEqual("-3.5", UsageCsv.Escape("-3.5"), "negative number untouched");
                        MuxAssert.AreEqual("-.5", UsageCsv.Escape("-.5"), "negative fraction untouched");
                        MuxAssert.AreEqual("\"'=a,b\"", UsageCsv.Escape("=a,b"), "guarded then quoted");
                        MuxAssert.AreEqual("'\tx", UsageCsv.Escape("\tx"), "tab guarded; the apostrophe means it no longer starts with a tab, so no quotes");
                        MuxAssert.AreEqual("a,\"b,c\"\nd,\n", UsageCsv.Write(new[] { "a", "b,c" }, new[] { new[] { "d", "" } }), "write");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "FileNamesAndMetrics", "File names, chart slugs, and the split metric keys", (CancellationToken ct) =>
                    {
                        DateTime at = new DateTime(2026, 11, 9, 14, 5, 9, DateTimeKind.Local);
                        MuxAssert.AreEqual("mux-usage-calls-day-20261109-140509.csv", UsageCsv.FileName(UsageCsv.ChartCalls, "day", at), "calls");
                        MuxAssert.AreEqual("mux-usage-calls-by-callkind-week-20261109-140509.csv", UsageCsv.FileName(UsageCsv.SplitChart("Calls", "callkind"), UsageWindow.KeyOf(UsageRange.Week), at), "split");
                        MuxAssert.AreEqual("mux-usage-usage-by-endpoint-all-20261109-140509.png", UsageCsv.FileName(UsageCsv.UsageByChart("endpoint"), "all", at, "png"), "png");
                        MuxAssert.AreEqual("tokens-by-model", UsageCsv.SplitChart("bogus", null), "fallbacks");
                        MuxAssert.AreEqual("tokens,calls,cost", string.Join(",", UsageCsv.Metrics), "metric order");
                        MuxAssert.AreEqual("cost", UsageCsv.NormalizeMetric(" COST "), "normalize");
                        UsageMetrics m = new UsageMetrics { Calls = 3, TotalTokens = 900, CostUsd = 0.5 };
                        MuxAssert.AreEqual(3.0, UsageCsv.MetricValue(m, "calls"), "calls value");
                        MuxAssert.AreEqual(0.5, UsageCsv.MetricValue(m, "cost"), "cost value");
                        MuxAssert.AreEqual(900.0, UsageCsv.MetricValue(m, null), "tokens value");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "OverTimeCharts", "Calls, tokens, cost, and a box plot export the exact expected text", (CancellationToken ct) =>
                    {
                        List<UsageBucket> buckets = Fixture();
                        TimeZoneInfo zone = Fixed();
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,succeeded,failed\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,3,1\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,0,0\n",
                            UsageCsv.Calls(buckets, 15 * MinuteMs, zone), "calls");
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,uncached_input,cached_input,output\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,800,200,250\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,0,0,0\n",
                            UsageCsv.Tokens(buckets, 15 * MinuteMs, zone), "tokens");
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,cost_usd\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,0.012346\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,0\n",
                            UsageCsv.Cost(buckets, 15 * MinuteMs, zone), "cost");
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,min,avg,p95,p99,max,count\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,120,450.5,900,980.25,1000,4\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,,,,,,0\n",
                            UsageCsv.Distribution(buckets, 15 * MinuteMs, m => m.TotalMsDist, zone), "latency");
                        MuxAssert.AreEqual("bucket_start,bucket_end,succeeded,failed\n", UsageCsv.Calls(null, 15 * MinuteMs, zone), "empty is header only");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "SplitAndUsageBy", "Split columns per value with Other last for each metric; Usage by rows", (CancellationToken ct) =>
                    {
                        TimeZoneInfo zone = Fixed();
                        List<long> starts = new List<long> { T0, T0 + 15 * MinuteMs };
                        List<UsageSeries> series = new List<UsageSeries>
                        {
                            Series("Other", true, (T0, 10, 1, 0.001)),
                            Series("gpt-4o", false, (T0, 500, 2, 0.02), (T0 + 15 * MinuteMs, 300, 1, 0.01)),
                            Series("", false, (T0 + 15 * MinuteMs, 40, 1, 0)),
                            Series("a,b", false)
                        };
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,gpt-4o,unknown,\"a,b\",Other\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,500,0,0,10\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,300,40,0,0\n",
                            UsageCsv.Split(starts, 15 * MinuteMs, series, "tokens", zone), "tokens by");
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,gpt-4o,unknown,\"a,b\",Other\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,2,0,0,1\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,1,1,0,0\n",
                            UsageCsv.Split(starts, 15 * MinuteMs, series, "calls", zone), "calls by");
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,gpt-4o,unknown,\"a,b\",Other\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,0.02,0,0,0.001\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,0.01,0,0,0\n",
                            UsageCsv.Split(starts, 15 * MinuteMs, series, "cost", zone), "cost by");

                        List<UsageBreakdownRow> rows = new List<UsageBreakdownRow>
                        {
                            new UsageBreakdownRow { Dimension = "endpoint", Value = "local", Metrics = new UsageMetrics { Calls = 5, Errors = 1, InputTokens = 1000, CachedTokens = 200, OutputTokens = 250, TotalTokens = 1250, CostUsd = 0.0123456 } },
                            new UsageBreakdownRow { Dimension = "endpoint", Value = "", Metrics = new UsageMetrics { Calls = 1 } }
                        };
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,endpoint,calls,failed,uncached_input,cached_input,output,total_tokens,cost_usd\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-10T14:00:00+05:30,local,5,1,800,200,250,1250,0.012346\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-10T14:00:00+05:30,unknown,1,0,0,0,0,0,0\n",
                            UsageCsv.UsageBy(T0, T0 + 1440 * MinuteMs, "endpoint", rows, zone), "usage by");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "Heatmap", "One row per cell inside the window, oldest first whatever the grid's display order; cells outside the window are skipped", (CancellationToken ct) =>
                    {
                        TimeZoneInfo zone = Fixed();
                        UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(UsageRange.Hour, T0 + 7 * MinuteMs, zone);
                        UsageHeatmap heatmap = new UsageHeatmap(layout);
                        long first = layout.CellStartUnixMs(layout.CellsInTimeOrder().First());
                        heatmap.Add(first, 42, 2);
                        heatmap.Add(first + MinuteMs, 7, 1);
                        string csv = UsageCsv.Heatmap(heatmap, zone);
                        string[] lines = csv.Split('\n');
                        int inside = Enumerable.Range(0, layout.CellCount).Count(i => !layout.IsOutsideWindow(i));
                        MuxAssert.AreEqual("cell_start,cell_end,tokens,calls", lines[0], "header");
                        MuxAssert.AreEqual(inside + 2, lines.Length, "one line per cell in the window plus header and trailing newline");
                        MuxAssert.AreEqual(UsageCsv.FormatTime(first, zone) + "," + UsageCsv.FormatTime(first + layout.BucketMs, zone) + ",42,2", lines[1], "first cell");
                        MuxAssert.AreEqual(UsageCsv.FormatTime(first + MinuteMs, zone) + "," + UsageCsv.FormatTime(first + 2 * MinuteMs, zone) + ",7,1", lines[2], "the next minute, though it is the next row of the grid");
                        MuxAssert.AreEqual(layout.Columns, layout.IndexAt(1, 0), "the second minute is a row down in display order");
                        for (int i = 2; i < lines.Length - 1; i++)
                        {
                            MuxAssert.IsTrue(string.CompareOrdinal(lines[i - 1], lines[i]) < 0, "chronological");
                        }

                        MuxAssert.IsTrue(inside < layout.CellCount, "the fixture has future cells");

                        // The year skips the days before its 364-day window as well as the future.
                        UsageHeatmapLayout year = UsageHeatmapLayout.Compute(UsageRange.Year, new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), 0);
                        MuxAssert.AreEqual(364 + 2, UsageCsv.Heatmap(new UsageHeatmap(year), TimeZoneInfo.Utc).Split('\n').Length, "a year exports its 364 days");
                        MuxAssert.AreEqual("cell_start,cell_end,tokens,calls\n", UsageCsv.Heatmap(null, zone), "no heatmap");
                        return Task.CompletedTask;
                    })
                });
        }

        /// <summary>The shared fixture: two 15-minute buckets from <see cref="T0"/>, the first busy, the second empty.</summary>
        /// <returns>The buckets.</returns>
        public static List<UsageBucket> Fixture()
        {
            return new List<UsageBucket>
            {
                new UsageBucket
                {
                    BucketStartUnixMs = T0,
                    Metrics = new UsageMetrics
                    {
                        Calls = 4, Errors = 1, InputTokens = 1000, CachedTokens = 200, OutputTokens = 250, TotalTokens = 1250, CostUsd = 0.0123456,
                        TotalMsDist = new UsageDistribution { Min = 120, Avg = 450.5, P95 = 900, P99 = 980.25, Max = 1000, Count = 4 }
                    }
                },
                new UsageBucket { BucketStartUnixMs = T0 + 15 * MinuteMs, Metrics = new UsageMetrics() }
            };
        }

        /// <summary>A fixed +05:30 zone without daylight time, so the expected text never depends on the host.</summary>
        /// <returns>The zone.</returns>
        public static TimeZoneInfo Fixed()
        {
            return TimeZoneInfo.CreateCustomTimeZone("Mux+0530", TimeSpan.FromMinutes(330), "Mux+0530", "Mux+0530");
        }

        private static TimeZoneInfo? NewYork()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }
            catch (TimeZoneNotFoundException)
            {
                return null;
            }
            catch (InvalidTimeZoneException)
            {
                return null;
            }
        }

        private static UsageSeries Series(string key, bool other, params (long Start, long Tokens, long Calls, double Cost)[] buckets)
        {
            UsageSeries series = new UsageSeries { Key = key, IsOther = other };
            foreach ((long start, long tokens, long calls, double cost) in buckets)
            {
                series.Buckets.Add(new UsageBucket { BucketStartUnixMs = start, Metrics = new UsageMetrics { TotalTokens = tokens, Calls = calls, CostUsd = cost } });
            }

            return series;
        }
    }
}
