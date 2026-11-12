namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the desktop usage window's testable chart shaping: range and dimension tables and their
    /// localized labels, bucket widths and drill-down windows, the calls/tokens/split stacks, failure reasons,
    /// "Usage by" rows, heatmap placement, time formats, the text-filter scope resolution, and the per-endpoint
    /// result merging used when the endpoint filter matches several endpoints.
    /// </summary>
    public static class UsageDesktopChartsSuite
    {
        private const string SuiteId = "UsageDesktopCharts";
        private const long MinuteMs = 60_000L;
        private const long DayMs = 1440L * MinuteMs;

        private static readonly string[] NewKeys =
        {
            "usage.range.quarter", "usage.range.year", "usage.range.all",
            "usage.range.quarter.tip", "usage.range.year.tip", "usage.range.all.tip",
            "usage.metric.calls", "usage.metric.calls.tip", "usage.metric.tokensBy", "usage.metric.tokensBy.tip",
            "usage.metric.usageBy", "usage.metric.usageBy.tip", "usage.metric.heatmap", "usage.metric.heatmap.tip",
            "usage.heatmap.title", "usage.heatmap.order", "usage.heatmap.less", "usage.heatmap.more", "usage.heatmap.peak",
            "usage.dim.label", "usage.dim.tip", "usage.dim.model", "usage.dim.endpoint", "usage.dim.provider", "usage.dim.callkind",
            "usage.legend.succeeded", "usage.legend.failed", "usage.legend.other",
            "usage.failures.title", "usage.failures.none", "usage.noUsage",
            "usage.tip.total", "usage.tip.max", "usage.tip.min", "usage.tip.samples", "usage.tip.drill",
            "usage.usageBy.tokens", "usage.usageBy.calls", "usage.drill.chip", "usage.drill.clear.tip",
            "usage.status.range", "usage.status.rangeAll", "usage.status.bucket", "usage.status.filteredNote", "usage.status.approx",
            "usage.metric.metricBy", "usage.splitMetric.label", "usage.splitMetric.tip", "usage.chart.bars", "usage.chart.lines",
            "usage.chart.bars.tip", "usage.chart.lines.tip", "usage.export.csv", "usage.export.csv.tip", "usage.export.png",
            "usage.export.png.tip", "usage.export.saved", "usage.export.failed", "usage.export.csvType", "usage.export.pngType",
            "usage.usageBy.sortedBy"
        };

        /// <summary>
        /// Builds the desktop usage-charts suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Desktop usage charts: shaping, drill-down, filters, and merging",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "RangesInOrderWithLabels", "Seven ranges in order, each with a label and tooltip key", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(7, UsageChartShaping.Ranges.Count, "range count");
                        MuxAssert.AreEqual("hour,day,week,month,quarter,year,all", string.Join(",", UsageChartShaping.Ranges.Select(UsageWindow.KeyOf)), "range order");
                        MuxAssert.AreEqual("usage.range.quarter", UsageChartShaping.RangeLabelKey(UsageRange.Quarter), "quarter label key");
                        MuxAssert.AreEqual("usage.range.all.tip", UsageChartShaping.RangeTipKey(UsageRange.All), "all tip key");

                        LocalizationService service = new LocalizationService();
                        service.SetActiveLocale("de");
                        MuxAssert.AreEqual("Quartal", service.Get(UsageChartShaping.RangeLabelKey(UsageRange.Quarter)), "German quarter");
                        foreach (UsageRange range in UsageChartShaping.Ranges)
                        {
                            MuxAssert.AreNotEqual(UsageChartShaping.RangeLabelKey(range), service.Get(UsageChartShaping.RangeLabelKey(range)), "label resolves for " + range);
                            MuxAssert.AreNotEqual(UsageChartShaping.RangeTipKey(range), service.Get(UsageChartShaping.RangeTipKey(range)), "tip resolves for " + range);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DimensionsAndLabels", "Four dimensions with model first; unknown dimensions normalize to model", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("model,endpoint,provider,callkind", string.Join(",", UsageChartShaping.Dimensions), "dimension order");
                        MuxAssert.AreEqual("model", UsageChartShaping.NormalizeDimension("label"), "label is not a picker dimension");
                        MuxAssert.AreEqual("callkind", UsageChartShaping.NormalizeDimension(" CallKind "), "case and whitespace");
                        MuxAssert.AreEqual("usage.dim.provider", UsageChartShaping.DimensionLabelKey("provider"), "label key");
                        MuxAssert.AreEqual("usage.dim.model", UsageChartShaping.DimensionLabelKey(null), "null maps to model");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "NewStringsTranslatedEverywhere", "Every new usage string has its own entry in every locale with the same placeholders", (CancellationToken ct) =>
                    {
                        Dictionary<string, Dictionary<string, string>> all = DesktopStrings.Build();
                        MuxAssert.AreEqual(11, all.Count, "locale count");
                        foreach (KeyValuePair<string, Dictionary<string, string>> locale in all)
                        {
                            foreach (string key in NewKeys)
                            {
                                MuxAssert.IsTrue(locale.Value.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value), locale.Key + ":" + key + " present");
                                MuxAssert.AreEqual(Placeholders(all["en"][key]), Placeholders(value!), locale.Key + ":" + key + " placeholders");
                            }
                        }

                        MuxAssert.AreNotEqual(all["en"]["usage.failures.none"], all["fr"]["usage.failures.none"], "French is translated, not copied");
                        MuxAssert.AreNotEqual(all["en"]["usage.metric.tokensBy"], all["zh"]["usage.metric.tokensBy"], "Chinese is translated, not copied");
                        MuxAssert.AreNotEqual(all["en"]["usage.export.csv"], all["de"]["usage.export.csv"], "German export is translated");
                        MuxAssert.AreNotEqual(all["en"]["usage.chart.lines"], all["ru"]["usage.chart.lines"], "Russian lines is translated");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "BucketWidthAndDrillWindow", "Bucket width comes from the first two buckets or the window; a drill window is [start, start + width - 1]", (CancellationToken ct) =>
                    {
                        UsageWindow quarter = UsageWindow.Compute(UsageRange.Quarter, 1_700_000_000_000L);
                        List<UsageBucket> two = new List<UsageBucket> { Bucket(1000, 1), Bucket(1000 + DayMs, 1) };
                        MuxAssert.AreEqual(DayMs, UsageChartShaping.BucketWidthMs(two, null), "from the first two buckets");
                        MuxAssert.AreEqual(DayMs, UsageChartShaping.BucketWidthMs(new List<UsageBucket> { Bucket(1000, 1) }, quarter), "single bucket uses the window grid");
                        MuxAssert.AreEqual(1L, UsageChartShaping.BucketWidthMs(null, null), "nothing known floors at 1");

                        long now = 1_700_000_000_000L;
                        UsageWindow all = UsageWindow.Compute(UsageRange.All, now, now - (200L * DayMs));
                        MuxAssert.AreEqual(7L * DayMs, UsageChartShaping.BucketWidthMs(null, all), "all-time width from the window");

                        (long from, long to) = UsageChartShaping.DrillWindow(quarter.FromUnixMs, quarter.BucketMs);
                        MuxAssert.AreEqual(quarter.FromUnixMs, from, "drill from");
                        MuxAssert.AreEqual(quarter.FromUnixMs + DayMs - 1, to, "drill to is inclusive and stops before the next bucket");
                        MuxAssert.AreEqual((5L, 5L), UsageChartShaping.DrillWindow(5, 0), "zero width floors at one millisecond");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "CallAndTokenStacks", "Calls split into succeeded and failed; tokens into uncached, cached, and output, floored at zero", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual((7L, 3L), UsageChartShaping.CallStack(new UsageMetrics { Calls = 10, Errors = 3 }), "calls minus errors");
                        MuxAssert.AreEqual((0L, 0L), UsageChartShaping.CallStack(null), "null bucket");
                        MuxAssert.AreEqual((0L, 2L), UsageChartShaping.CallStack(new UsageMetrics { Calls = 1, Errors = 2 }), "succeeded floored");

                        (long uncached, long cached, long output, long total) = UsageChartShaping.TokenStack(new UsageMetrics { InputTokens = 100, CachedTokens = 30, OutputTokens = 20 });
                        MuxAssert.AreEqual(70L, uncached, "uncached = input - cached");
                        MuxAssert.AreEqual(30L, cached, "cached");
                        MuxAssert.AreEqual(20L, output, "output");
                        MuxAssert.AreEqual(120L, total, "stack total");
                        MuxAssert.AreEqual(0L, UsageChartShaping.TokenStack(new UsageMetrics { InputTokens = 10, CachedTokens = 40 }).Uncached, "uncached floored at zero");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "FailureReasons", "Failure reasons combine blanks as unknown, drop zeros, and sort most frequent first", (CancellationToken ct) =>
                    {
                        List<UsageErrorCount> reasons = UsageChartShaping.FailureReasons(new List<UsageErrorCount>
                        {
                            new UsageErrorCount { ErrorCode = "timeout", Count = 2 },
                            new UsageErrorCount { ErrorCode = "", Count = 1 },
                            new UsageErrorCount { ErrorCode = "unknown", Count = 2 },
                            new UsageErrorCount { ErrorCode = "http_500", Count = 5 },
                            new UsageErrorCount { ErrorCode = "rate_limit", Count = 0 }
                        });
                        MuxAssert.AreEqual("http_500:5,unknown:3,timeout:2", string.Join(",", reasons.Select(r => r.ErrorCode + ":" + r.Count)), "reasons");
                        MuxAssert.AreEqual(0, UsageChartShaping.FailureReasons(null).Count, "null is empty");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "SplitStackOrdering", "Split series keep rank colors, fold extras into one Other, and put Other last in grey", (CancellationToken ct) =>
                    {
                        List<UsageSeries> series = new List<UsageSeries>();
                        for (int i = 0; i < 8; i++)
                        {
                            series.Add(Series("m" + i, false, 100 - i, 50 - i));
                        }

                        series.Add(Series("Other", true, 5, 5));
                        List<(UsageSeries Series, int ColorIndex)> stack = UsageChartShaping.SplitStack(series, 6);
                        MuxAssert.AreEqual(7, stack.Count, "six named plus Other");
                        MuxAssert.AreEqual("m0,m1,m2,m3,m4,m5,Other", string.Join(",", stack.Select(s => s.Series.Key)), "order");
                        MuxAssert.AreEqual("0,1,2,3,4,5,-1", string.Join(",", stack.Select(s => s.ColorIndex)), "rank colors, Other neutral");
                        MuxAssert.AreEqual((94L + 93L + 5L) + (44L + 43L + 5L), stack[6].Series.Buckets.Sum(b => b.Metrics.TotalTokens), "extras folded into Other");

                        long max = UsageChartShaping.SplitMax(stack.Select(s => s.Series));
                        MuxAssert.AreEqual(Enumerable.Range(0, 8).Sum(i => 100L - i) + 5L, max, "tallest column is the per-bucket sum");
                        MuxAssert.AreEqual(0, UsageChartShaping.SplitStack(null).Count, "null is empty");
                        MuxAssert.AreEqual(0L, UsageChartShaping.SplitMax(null), "null max");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "UsageByRows", "Usage-by rows sort by cost then tokens, drop empty rows, and stop at ten", (CancellationToken ct) =>
                    {
                        List<UsageBreakdownRow> rows = new List<UsageBreakdownRow>
                        {
                            Row("cheap-big", 0.1, 9000, 3),
                            Row("pricey", 2.0, 100, 1),
                            Row("cheap-small", 0.1, 10, 1),
                            Row("empty", 0, 0, 0)
                        };
                        for (int i = 0; i < 12; i++)
                        {
                            rows.Add(Row("free-" + i.ToString("00", CultureInfo.InvariantCulture), 0, 5 + i, 1));
                        }

                        List<UsageBreakdownRow> top = UsageChartShaping.TopBreakdown(rows);
                        MuxAssert.AreEqual(10, top.Count, "at most ten");
                        MuxAssert.AreEqual("pricey,cheap-big,cheap-small,free-11", string.Join(",", top.Take(4).Select(r => r.Value)), "cost then tokens");
                        MuxAssert.IsFalse(top.Any(r => r.Value == "empty"), "empty rows dropped");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "HeatmapShadingAndAxes", "Heatmap cells shade by quartile with a grey no-usage fill, and the axes get a localized caption and thinned row labels", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("#e3e7eb", UsageChartShaping.HeatmapFill(0, false), "no usage is a light grey (light)");
                        MuxAssert.AreEqual("#2e3640", UsageChartShaping.HeatmapFill(0, true), "no usage is a dark grey (dark)");
                        MuxAssert.AreEqual("#6cc596", UsageChartShaping.HeatmapFill(1, false), "lightest step on light");
                        MuxAssert.AreEqual("#0a4d33", UsageChartShaping.HeatmapFill(4, false), "darkest step on light");
                        MuxAssert.AreEqual("#1f6b48", UsageChartShaping.HeatmapFill(1, true), "darkest step on dark");
                        MuxAssert.AreEqual("#9be8bd", UsageChartShaping.HeatmapFill(4, true), "brightest step on dark");
                        MuxAssert.AreEqual(UsageHeatmapScale.Levels, UsageChartShaping.HeatmapRampLight.Count, "a light step per level");
                        MuxAssert.AreEqual(UsageHeatmapScale.Levels, UsageChartShaping.HeatmapRampDark.Count, "a dark step per level");

                        long now = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        Dictionary<string, string> en = DashboardStrings.Build()["en"];
                        Dictionary<string, string> de = DashboardStrings.Build()["de"];
                        UsageHeatmapLayout day = UsageHeatmapLayout.Compute(UsageRange.Day, now, 0);
                        MuxAssert.AreEqual(day.Caption, UsageChartShaping.HeatmapCaption(day, k => en[k]), "English caption matches Mux.Core's");
                        MuxAssert.AreEqual("Columns: hour of day · Rows: minute of the hour", UsageChartShaping.HeatmapCaption(day, k => en[k]), "x first, then y");
                        MuxAssert.AreEqual("Spalten: Stunde des Tages · Zeilen: Minute der Stunde", UsageChartShaping.HeatmapCaption(day, k => de[k]), "German caption");
                        foreach (UsageRange range in new[] { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month, UsageRange.Quarter, UsageRange.Year })
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(range, now, 0);
                            MuxAssert.AreEqual(layout.Caption, UsageChartShaping.HeatmapCaption(layout, k => en[k]), "caption for " + range);
                            double height = UsageChartShaping.HeatmapRowHeight(layout.Rows, layout.Columns);
                            MuxAssert.IsTrue(height >= 14 && height <= 30, "row height in range for " + range);
                            MuxAssert.IsTrue(layout.Rows * height <= 360, "the grid stays short for " + range);
                            List<int> labeled = UsageChartShaping.HeatmapLabeledRows(layout.Rows, height);
                            MuxAssert.AreEqual(layout.Rows, labeled.Count, "every row labeled for " + range);
                            foreach (KeyValuePair<string, Dictionary<string, string>> pack in DashboardStrings.Build())
                            {
                                MuxAssert.IsTrue(pack.Value.ContainsKey(UsageChartShaping.HeatmapColumnKey(layout.ColumnUnit)), "x caption for " + range + " in " + pack.Key);
                                MuxAssert.IsTrue(pack.Value.ContainsKey(UsageChartShaping.HeatmapRowKey(layout.RowKind)), "y caption for " + range + " in " + pack.Key);
                            }
                        }

                        foreach (long span in new[] { 50 * MinuteMs, DayMs, 40L * DayMs, 400L * DayMs })
                        {
                            UsageHeatmapLayout all = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - span);
                            MuxAssert.AreEqual(all.Caption, UsageChartShaping.HeatmapCaption(all, k => en[k]), "all-time caption");
                            MuxAssert.IsTrue(DashboardStrings.Build()["de"].ContainsKey(UsageChartShaping.HeatmapRowKey(all.RowKind)), "all-time y caption in German");
                        }

                        UsageHeatmapLayout month = UsageHeatmapLayout.Compute(UsageRange.Month, now, 0);
                        MuxAssert.AreEqual("us.hm.x.halfday", UsageChartShaping.HeatmapColumnKey(month.ColumnUnit), "half-day column key");
                        MuxAssert.AreEqual("us.hm.y.week", UsageChartShaping.HeatmapRowKey(month.RowKind), "week row key");
                        MuxAssert.AreEqual("Columns: day of the week (AM, PM) · Rows: week starting", UsageChartShaping.HeatmapCaption(month, k => en[k]), "English month caption");
                        MuxAssert.AreEqual("Spalten: Wochentag (vormittags, nachmittags) · Zeilen: Woche (Beginn)", UsageChartShaping.HeatmapCaption(month, k => de[k]), "German month caption");
                        MuxAssert.AreEqual("us.hm.x.period", UsageChartShaping.HeatmapColumnKey("bogus"), "unknown column unit");
                        MuxAssert.AreEqual("us.hm.y.weeks", UsageChartShaping.HeatmapRowKey(null), "unknown row kind");

                        // Rows of a wide grid are short; row labels thin only when rows are under 14 pixels.
                        MuxAssert.AreEqual(16.0, UsageChartShaping.HeatmapRowHeight(7, 53), "a year's rows are about square with its 53 columns");
                        MuxAssert.AreEqual(30.0, UsageChartShaping.HeatmapRowHeight(4, 24), "a day's rows are capped at 30 pixels");
                        MuxAssert.IsTrue(UsageChartShaping.HeatmapLabeledRows(12, 10).Count < 12, "short rows thin their labels");
                        MuxAssert.AreEqual(11, UsageChartShaping.HeatmapLabeledRows(12, 10)[UsageChartShaping.HeatmapLabeledRows(12, 10).Count - 1], "the last row keeps its label");

                        // x labels: how many columns one label needs, fed to the layout's thinning.
                        MuxAssert.AreEqual(1, UsageChartShaping.HeatmapLabelGap(14, 900, 24), "two-digit hours fit one 37 pixel column");
                        MuxAssert.AreEqual(3, UsageChartShaping.HeatmapLabelGap(20, 900, 100), "a 20 pixel label over 9 pixel columns needs 3");
                        MuxAssert.AreEqual(5, UsageChartShaping.HeatmapLabelGap(10, 0, 5), "no width: one label");
                        UsageHeatmapLayout year = UsageHeatmapLayout.Compute(UsageRange.Year, now, 0);
                        List<int> picked = year.LabeledColumns(UsageChartShaping.HeatmapLabelGap(22, 800, year.Columns));
                        MuxAssert.AreEqual(0, picked[0], "year: first month labeled");
                        MuxAssert.AreEqual(year.Columns - 1, picked[picked.Count - 1], "year: last column labeled");

                        // Where each label's text goes (cells inset 1.5 pixels in their columns): a weekday centred over
                        // its AM and PM cells, Sun over Sun AM and PM rather than pushed to the grid's end.
                        long wednesday = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageHeatmapLayout calendar = UsageHeatmapLayout.Compute(UsageRange.Month, wednesday, 0);
                        foreach (double width in new[] { 400.0, 700.0, 1400.0 })
                        {
                            double pitch = width / 14;
                            List<UsageHeatmapLabel> days = calendar.PlaceColumnLabels(UsageChartShaping.HeatmapLabelGap(22, width, 14));
                            UsageHeatmapLabel sun = days[days.Count - 1];
                            MuxAssert.AreEqual(12, sun.Column, "Sun is labeled at " + width);
                            double x = UsageChartShaping.HeatmapLabelLeft(sun, width, 14, 22);
                            MuxAssert.IsTrue(Math.Abs((x + 11) - (13 * pitch)) < 0.01, "Sun centred over Sun AM and PM at " + width + ": " + x);
                            MuxAssert.IsTrue(x + 22 < width - 1, "Sun does not reach the grid's end at " + width);
                            foreach (UsageHeatmapLabel weekday in days)
                            {
                                double left = UsageChartShaping.HeatmapLabelLeft(weekday, width, 14, 22);
                                MuxAssert.IsTrue(Math.Abs((left + 11) - ((weekday.Column + 1) * pitch)) < 0.01, calendar.ColumnLabels[weekday.Column] + " centred over its half-days at " + width);
                            }
                        }

                        UsageHeatmapLayout dayRange = UsageHeatmapLayout.Compute(UsageRange.Day, wednesday, 0);
                        UsageHeatmapLabel lastHour = dayRange.PlaceColumnLabels(3)[dayRange.PlaceColumnLabels(3).Count - 1];
                        MuxAssert.AreEqual(23, lastHour.Column, "day: the last hour labeled");
                        MuxAssert.AreEqual(720.0 - 1.5 - 14, UsageChartShaping.HeatmapLabelLeft(lastHour, 720, 24, 14), "day: the last hour ends at its own cell");
                        MuxAssert.AreEqual(30.0 * 3 + 1.5, UsageChartShaping.HeatmapLabelLeft(dayRange.PlaceColumnLabels(3)[1], 720, 24, 14), "day: a label starts at its own cell");
                        MuxAssert.AreEqual(0.0, UsageChartShaping.HeatmapLabelLeft(new UsageHeatmapLabel(0, 2, UsageHeatmapLabelAlignEnum.Center), 30, 14, 22), "text wider than its room stays inside the plot");
                        MuxAssert.AreEqual("Mo", UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 0).FormatRowLabel(0, new System.Globalization.CultureInfo("de-DE")), "weekday rows follow the culture");
                        MuxAssert.AreEqual("Do 05.11", UsageHeatmapLayout.Compute(UsageRange.Week, now, 0).FormatColumnLabel(0, new System.Globalization.CultureInfo("de-DE")), "weekday and date columns follow the culture");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "TimeFormats", "Axis labels use dates for long ranges, and bucket windows read naturally", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("HH:mm", UsageChartShaping.AxisFormat(UsageRange.Day, 15 * MinuteMs), "day");
                        MuxAssert.AreEqual("ddd HH:mm", UsageChartShaping.AxisFormat(UsageRange.Week, 120 * MinuteMs), "week");
                        MuxAssert.AreEqual("MMM d", UsageChartShaping.AxisFormat(UsageRange.Quarter, DayMs), "quarter is dates");
                        MuxAssert.AreEqual("MMM d, yyyy", UsageChartShaping.AxisFormat(UsageRange.Year, 7 * DayMs), "year has the year");
                        MuxAssert.AreEqual("MMM d, yyyy", UsageChartShaping.AxisFormat(UsageRange.All, 7 * DayMs), "long all-time is dates");
                        MuxAssert.AreEqual("MMM d HH:mm", UsageChartShaping.AxisFormat(UsageRange.All, 5 * MinuteMs), "a young store keeps times");

                        long start = new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        CultureInfo inv = CultureInfo.InvariantCulture;
                        MuxAssert.AreEqual("Nov 11 14:00–14:15", UsageChartShaping.FormatBucketWindow(start, 15 * MinuteMs, TimeZoneInfo.Utc, inv), "sub-day bucket");
                        long midnight = new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        MuxAssert.AreEqual("Nov 11, 2026", UsageChartShaping.FormatBucketWindow(midnight, DayMs, TimeZoneInfo.Utc, inv), "one-day bucket");
                        MuxAssert.AreEqual("Nov 11 – Nov 17, 2026", UsageChartShaping.FormatBucketWindow(midnight, 7 * DayMs, TimeZoneInfo.Utc, inv), "one-week bucket");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "SplitMetricPickerShaping", "Metric picker: tokens, calls, cost; labels; stacks, alignment, and Usage by ranking follow the metric", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("tokens,calls,cost", string.Join(",", UsageChartShaping.SplitMetrics), "metric order");
                        MuxAssert.AreEqual("usage.metric.calls", UsageChartShaping.SplitMetricLabelKey("calls"), "label key reuses the tab name");
                        MuxAssert.AreEqual("usage.metric.tokens", UsageChartShaping.SplitMetricLabelKey(null), "default label");

                        LocalizationService service = new LocalizationService();
                        MuxAssert.AreEqual("Calls by Call kind", string.Format(CultureInfo.InvariantCulture, service.Get("usage.metric.metricBy"), service.Get(UsageChartShaping.SplitMetricLabelKey("calls")), service.Get(UsageChartShaping.DimensionLabelKey("callkind"))), "English title (R17)");
                        service.SetActiveLocale("de");
                        MuxAssert.AreEqual("Kosten nach Modell", string.Format(CultureInfo.InvariantCulture, service.Get("usage.metric.metricBy"), service.Get(UsageChartShaping.SplitMetricLabelKey("cost")), service.Get(UsageChartShaping.DimensionLabelKey("model"))), "German title");

                        UsageSeries a = new UsageSeries { Key = "a", Buckets = new List<UsageBucket> { Metric(0, 100, 2, 0.5), Metric(MinuteMs, 50, 6, 0.25) } };
                        UsageSeries b = new UsageSeries { Key = "b", Buckets = new List<UsageBucket> { Metric(MinuteMs, 10, 1, 1.0) } };
                        MuxAssert.AreEqual(100.0, UsageChartShaping.SplitMax(new[] { a, b }, "tokens"), "tokens stack max");
                        MuxAssert.AreEqual(7.0, UsageChartShaping.SplitMax(new[] { a, b }, "calls"), "calls stack max");
                        MuxAssert.AreEqual(1.25, UsageChartShaping.SplitMax(new[] { a, b }, "cost"), "cost stack max");
                        List<UsageBucket> grid = new List<UsageBucket> { Bucket(0, 0), Bucket(MinuteMs, 0), Bucket(2 * MinuteMs, 0) };
                        MuxAssert.AreEqual("0,1,0", string.Join(",", UsageChartShaping.AlignSeries(b, grid, "calls")), "aligned by start; missing buckets are 0");
                        MuxAssert.AreEqual(3, UsageChartShaping.AlignSeries(null, grid, "calls").Length, "null series");

                        List<UsageBreakdownRow> rows = new List<UsageBreakdownRow> { Row("pricey", 3, 100, 1), Row("big", 1, 9000, 2), Row("chatty", 0.1, 50, 40), Row("idle", 0, 0, 0) };
                        MuxAssert.AreEqual("big,pricey,chatty", string.Join(",", UsageChartShaping.RankBreakdown(rows, "tokens").Select(r => r.Value)), "by tokens; idle dropped");
                        MuxAssert.AreEqual("chatty,big,pricey", string.Join(",", UsageChartShaping.RankBreakdown(rows, "calls").Select(r => r.Value)), "by calls");
                        MuxAssert.AreEqual("pricey,big,chatty", string.Join(",", UsageChartShaping.RankBreakdown(rows, "cost").Select(r => r.Value)), "by cost");
                        MuxAssert.AreEqual(1, UsageChartShaping.RankBreakdown(rows, "cost", 1).Count, "capped");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "LinesModeGeometry", "Lines are unstacked: scale is the largest single value, points sit at bucket centers, markers only with few buckets", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(9.0, UsageChartShaping.LineMax(new[] { new double[] { 1, 9 }, new double[] { 8, 8 } }), "largest single value, not the stack (17)");
                        MuxAssert.AreEqual(0.0, UsageChartShaping.LineMax(null), "no series");
                        List<(double X, double Y)> points = UsageChartShaping.LinePoints(new double[] { 0, 5, 10, 20 }, 400, 100, 10);
                        MuxAssert.AreEqual("50:100,150:50,250:0,350:0", string.Join(",", points.Select(p => p.X.ToString(CultureInfo.InvariantCulture) + ":" + p.Y.ToString(CultureInfo.InvariantCulture))), "centers, heights, clamped at the top");
                        MuxAssert.AreEqual(100.0, UsageChartShaping.LinePoints(new double[] { 3 }, 10, 100, 0)[0].Y, "zero scale sits on the baseline");
                        MuxAssert.AreEqual(0, UsageChartShaping.LinePoints(null, 10, 10, 1).Count, "no values");
                        MuxAssert.IsTrue(UsageChartShaping.ShowLineMarkers(7), "week of days gets markers");
                        MuxAssert.IsTrue(UsageChartShaping.ShowLineMarkers(UsageChartShaping.LineMarkerMaxBuckets), "at the limit");
                        MuxAssert.IsFalse(UsageChartShaping.ShowLineMarkers(96), "dense ranges do not");
                        MuxAssert.IsFalse(UsageChartShaping.ShowLineMarkers(0), "nothing to mark");
                        foreach (string kind in new[] { UsageChartExport.Calls, UsageChartExport.Tokens, UsageChartExport.Split, UsageChartExport.Cost }) MuxAssert.IsTrue(UsageChartExport.IsStacked(kind), kind + " toggles");
                        foreach (string kind in new[] { UsageChartExport.Latency, UsageChartExport.Ttft, UsageChartExport.Streaming, UsageChartExport.Throughput, UsageChartExport.UsageBy, UsageChartExport.Heatmap }) MuxAssert.IsFalse(UsageChartExport.IsStacked(kind), kind + " has no toggle");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ExportCsvAndFileNames", "Every chart exports the shared CSV format, names files by chart and range, and has nothing to export without data", (CancellationToken ct) =>
                    {
                        TimeZoneInfo zone = UsageCsvSuite.Fixed();
                        long t0 = UsageCsvSuite.Fixture()[0].BucketStartUnixMs;
                        UsageWindow window = UsageWindow.Compute(UsageRange.Day, t0);
                        UsageDashboardData data = new UsageDashboardData(UsageRange.Day, window) { Dimension = "callkind" };
                        data.Timeseries.AddRange(UsageCsvSuite.Fixture());
                        data.Split.Add(new UsageSeries { Key = "Other", IsOther = true, Buckets = new List<UsageBucket> { Metric(t0, 10, 1, 0.01) } });
                        data.Split.Add(new UsageSeries { Key = "turn", Buckets = new List<UsageBucket> { Metric(t0, 1000, 3, 0.02), Metric(t0 + 15 * MinuteMs, 0, 0, 0) } });
                        data.Breakdown.Add(Row("turn", 0.02, 1000, 3));
                        data.Breakdown.Add(Row("title", 0.001, 50, 9));

                        MuxAssert.AreEqual(UsageCsv.Calls(data.Timeseries, 15 * MinuteMs, zone), UsageChartExport.BuildCsv(data, UsageChartExport.Calls, "tokens", zone), "calls");
                        MuxAssert.AreEqual(UsageCsv.Tokens(data.Timeseries, 15 * MinuteMs, zone), UsageChartExport.BuildCsv(data, UsageChartExport.Tokens, "tokens", zone), "tokens");
                        MuxAssert.AreEqual(UsageCsv.Cost(data.Timeseries, 15 * MinuteMs, zone), UsageChartExport.BuildCsv(data, UsageChartExport.Cost, "tokens", zone), "cost");
                        MuxAssert.Contains(",120,450.5,900,980.25,1000,4\n", UsageChartExport.BuildCsv(data, UsageChartExport.Latency, null, zone), "latency box");
                        MuxAssert.AreEqual(
                            "bucket_start,bucket_end,turn,Other\n"
                            + "2026-11-09T14:00:00+05:30,2026-11-09T14:15:00+05:30,3,1\n"
                            + "2026-11-09T14:15:00+05:30,2026-11-09T14:30:00+05:30,0,0\n",
                            UsageChartExport.BuildCsv(data, UsageChartExport.Split, "calls", zone), "calls by call kind, Other last");
                        string? usageBy = UsageChartExport.BuildCsv(data, UsageChartExport.UsageBy, "calls", zone);
                        MuxAssert.IsTrue(usageBy!.StartsWith("bucket_start,bucket_end,callkind,calls,", StringComparison.Ordinal), "usage by header names the dimension");
                        MuxAssert.IsTrue(usageBy.IndexOf(",title,", StringComparison.Ordinal) < usageBy.IndexOf(",turn,", StringComparison.Ordinal), "usage by ranked by the metric");
                        MuxAssert.IsNull(UsageChartExport.BuildCsv(data, UsageChartExport.Heatmap, null, zone), "no heatmap, nothing to export");
                        MuxAssert.IsFalse(UsageChartExport.HasData(data, UsageChartExport.Heatmap), "heatmap export disabled");

                        UsageDashboardData empty = new UsageDashboardData(UsageRange.Day, window);
                        empty.Timeseries.Add(new UsageBucket { BucketStartUnixMs = t0, Metrics = new UsageMetrics() });
                        foreach (string kind in new[] { UsageChartExport.Calls, UsageChartExport.Split, UsageChartExport.UsageBy, UsageChartExport.Latency })
                        {
                            MuxAssert.IsFalse(UsageChartExport.HasData(empty, kind), kind + " disabled without usage");
                            MuxAssert.IsNull(UsageChartExport.BuildCsv(empty, kind, null, zone), kind + " exports nothing");
                        }

                        MuxAssert.IsFalse(UsageChartExport.HasData(null, UsageChartExport.Calls), "no data loaded");

                        DateTime at = new DateTime(2026, 11, 9, 14, 5, 9, DateTimeKind.Local);
                        MuxAssert.AreEqual("mux-usage-calls-by-callkind-week-20261109-140509.csv", UsageChartExport.FileName(UsageChartExport.Split, "calls", "callkind", UsageRange.Week, at), "split csv name");
                        MuxAssert.AreEqual("mux-usage-usage-by-endpoint-all-20261109-140509.png", UsageChartExport.FileName(UsageChartExport.UsageBy, "cost", "endpoint", UsageRange.All, at, "png"), "png name");
                        MuxAssert.AreEqual("mux-usage-streaming-quarter-20261109-140509.csv", UsageChartExport.FileName(UsageChartExport.Streaming, null, null, UsageRange.Quarter, at), "box plot name");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "ScopeMatching", "Endpoint and conversation text filters resolve to exact names and session ids", (CancellationToken ct) =>
                    {
                        List<string> endpoints = new List<string> { "openai-prod", "OpenAI-dev", "ollama", "" };
                        MuxAssert.IsNull(UsageScope.MatchEndpoints("  ", endpoints), "blank is any endpoint");
                        MuxAssert.AreEqual("OpenAI-dev,openai-prod", string.Join(",", UsageScope.MatchEndpoints("openai", endpoints)!), "case-insensitive contains");
                        MuxAssert.AreEqual(0, UsageScope.MatchEndpoints("azure", endpoints)!.Count, "no match is empty, not any");

                        Dictionary<string, string> conversations = new Dictionary<string, string>
                        {
                            { "abcdef1234567890", "Refactor parser" },
                            { "fedcba0987654321", "" },
                            { "1111222233334444", "Parser tests" }
                        };
                        MuxAssert.IsNull(UsageScope.MatchSessions(null, conversations), "blank is any conversation");
                        MuxAssert.AreEqual(2, UsageScope.MatchSessions("parser", conversations)!.Count, "matches titles");
                        MuxAssert.IsTrue(UsageScope.MatchSessions("fedcba", conversations)!.Contains("fedcba0987654321"), "untitled matches the id prefix shown in the table");
                        MuxAssert.AreEqual(0, UsageScope.MatchSessions("abcdef1234", conversations)!.Count, "titled conversations match by title, as the table shows");
                        MuxAssert.IsTrue(UsageScope.MatchSessions("ABCDEF1234567890", conversations)!.Contains("abcdef1234567890"), "a full session id matches");

                        MuxAssert.AreEqual("fedcba09", UsageScope.ConversationLabel("fedcba0987654321", " "), "id prefix label");
                        MuxAssert.AreEqual("—", UsageScope.ConversationLabel(null, null), "no session");
                        MuxAssert.IsTrue(new UsageScope(new List<string>()).MatchesNothing, "empty endpoint set matches nothing");
                        MuxAssert.IsFalse(UsageScope.Any.MatchesNothing, "any matches everything");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MergeMetricsAndDistributions", "Combining endpoints sums totals exactly and weights averages by sample count", (CancellationToken ct) =>
                    {
                        UsageMetrics a = new UsageMetrics { Calls = 3, Errors = 1, InputTokens = 100, CachedTokens = 50, OutputTokens = 10, TotalTokens = 110, CostUsd = 1, AvgTotalMs = 100 };
                        a.TotalMsDist = new UsageDistribution { Count = 3, Min = 50, Avg = 100, P95 = 140, P99 = 150, Max = 150 };
                        UsageMetrics b = new UsageMetrics { Calls = 1, Errors = 1, InputTokens = 100, TotalTokens = 100, CostUsd = 2, AvgTotalMs = 500 };
                        b.TotalMsDist = new UsageDistribution { Count = 1, Min = 500, Avg = 500, P95 = 500, P99 = 500, Max = 500 };

                        UsageMetrics m = UsageMerge.MergeMetrics(new[] { a, b });
                        MuxAssert.AreEqual(4L, m.Calls, "calls");
                        MuxAssert.AreEqual(2L, m.Errors, "errors");
                        MuxAssert.AreEqual(0.5, m.ErrorRate, "error rate recomputed");
                        MuxAssert.AreEqual(0.25, m.CacheHitRate, "cache hit rate recomputed");
                        MuxAssert.AreEqual(3.0, m.CostUsd, "cost");
                        MuxAssert.AreEqual(200.0, m.AvgTotalMs, "average weighted by samples");
                        MuxAssert.AreEqual(4L, m.TotalMsDist.Count, "samples");
                        MuxAssert.AreEqual(50.0, m.TotalMsDist.Min, "min");
                        MuxAssert.AreEqual(500.0, m.TotalMsDist.Max, "max");
                        MuxAssert.AreEqual(200.0, m.TotalMsDist.Avg, "dist mean");
                        MuxAssert.AreEqual(0L, UsageMerge.MergeDistributions(new UsageDistribution?[] { null, new UsageDistribution() }).Count, "empty parts");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MergeSplitErrorsBreakdownEvents", "Combining endpoints re-ranks split series, sums errors and breakdowns, and interleaves event pages", (CancellationToken ct) =>
                    {
                        List<UsageSeries> p1 = new List<UsageSeries> { Series("m1", false, 10, 10), Series("m2", false, 5, 5) };
                        List<UsageSeries> p2 = new List<UsageSeries> { Series("m2", false, 30, 0), Series("m3", false, 1, 1), Series("Other", true, 2, 2) };
                        List<UsageSeries> merged = UsageMerge.MergeSplit(new[] { p1, p2 }, 2);
                        MuxAssert.AreEqual("m2,m1,Other", string.Join(",", merged.Select(s => s.Key)), "re-ranked by tokens, extras folded");
                        MuxAssert.IsTrue(merged[2].IsOther, "Other flagged");
                        MuxAssert.AreEqual(6L, merged[2].Buckets.Sum(b => b.Metrics.TotalTokens), "Other holds m3 and the parts' Other");
                        MuxAssert.AreEqual(2, merged[0].Buckets.Count, "buckets stay aligned");

                        List<UsageErrorCount> errors = UsageMerge.MergeErrors(new[]
                        {
                            new List<UsageErrorCount> { new UsageErrorCount { ErrorCode = "timeout", Count = 1 } },
                            new List<UsageErrorCount> { new UsageErrorCount { ErrorCode = "timeout", Count = 2 }, new UsageErrorCount { ErrorCode = "http_500", Count = 4 } }
                        });
                        MuxAssert.AreEqual("http_500:4,timeout:3", string.Join(",", errors.Select(e => e.ErrorCode + ":" + e.Count)), "errors combined");

                        List<UsageBreakdownRow> breakdown = UsageMerge.MergeBreakdown(new[]
                        {
                            new List<UsageBreakdownRow> { Row("m1", 1, 10, 1) },
                            new List<UsageBreakdownRow> { Row("m1", 1, 10, 1), Row("m2", 1.5, 5, 1) }
                        });
                        MuxAssert.AreEqual("m1:2,m2:1", string.Join(",", breakdown.Select(r => r.Value + ":" + r.Metrics.Calls)), "breakdown combined and sorted by cost");

                        UsageEventPage page = UsageMerge.MergeEventPages(new[]
                        {
                            new UsageEventPage { Items = new List<UsageEventRow> { Event(1, 300), Event(2, 100) }, TotalCount = 5 },
                            new UsageEventPage { Items = new List<UsageEventRow> { Event(3, 200) }, TotalCount = 1 }
                        }, 2);
                        MuxAssert.AreEqual("1,3", string.Join(",", page.Items.Select(r => r.Id)), "newest first, limited to the page");
                        MuxAssert.AreEqual(6L, page.TotalCount, "totals summed");
                        return Task.CompletedTask;
                    })
                });
        }

        private static string Placeholders(string text)
        {
            List<string> found = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                if (text.Contains("{" + i.ToString(CultureInfo.InvariantCulture) + "}", StringComparison.Ordinal))
                {
                    found.Add(i.ToString(CultureInfo.InvariantCulture));
                }
            }

            return string.Join(",", found);
        }

        private static UsageBucket Bucket(long start, long tokens)
        {
            return new UsageBucket { BucketStartUnixMs = start, Metrics = new UsageMetrics { Calls = 1, TotalTokens = tokens } };
        }

        private static UsageSeries Series(string key, bool isOther, long first, long second)
        {
            return new UsageSeries
            {
                Key = key,
                IsOther = isOther,
                Buckets = new List<UsageBucket> { Bucket(0, first), Bucket(MinuteMs, second) }
            };
        }

        private static UsageBucket Metric(long start, long tokens, long calls, double cost)
        {
            return new UsageBucket { BucketStartUnixMs = start, Metrics = new UsageMetrics { TotalTokens = tokens, Calls = calls, CostUsd = cost } };
        }

        private static UsageBreakdownRow Row(string value, double cost, long tokens, long calls)
        {
            return new UsageBreakdownRow
            {
                Dimension = "model",
                Value = value,
                Metrics = new UsageMetrics { CostUsd = cost, TotalTokens = tokens, InputTokens = tokens, Calls = calls }
            };
        }

        private static UsageEventRow Event(long id, long ts)
        {
            return new UsageEventRow { Id = id, TimestampUnixMs = ts };
        }
    }
}
