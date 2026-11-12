namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Pure data shaping for the desktop usage charts, kept out of the Avalonia window so it can be tested:
    /// range and dimension tables, bucket widths and drill-down windows, the per-bucket stacks (calls,
    /// tokens, split series by tokens, calls, or cost), the failure-reason list, the "Usage by" rows ranked by a
    /// metric, the lines-mode geometry, heatmap axis captions, row labels, and shading, and axis/tooltip time formats.
    /// </summary>
    public static class UsageChartShaping
    {
        #region Public-Members

        /// <summary>How many named values the "Tokens by" chart keeps before folding the rest into "Other".</summary>
        public const int SplitTop = 6;

        /// <summary>How many rows the "Usage by" chart shows.</summary>
        public const int BreakdownRows = 10;


        /// <summary>The seven ranges, in display order.</summary>
        public static IReadOnlyList<UsageRange> Ranges { get; } = new[]
        {
            UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month, UsageRange.Quarter, UsageRange.Year, UsageRange.All
        };

        /// <summary>The dimensions the "Tokens by" and "Usage by" charts can group by, in picker order (model is the default).</summary>
        public static IReadOnlyList<string> Dimensions { get; } = new[] { "model", "endpoint", "provider", "callkind" };

        #endregion

        #region Public-Methods

        /// <summary>The localization key of a range's button label, for example "usage.range.quarter".</summary>
        /// <param name="range">The range.</param>
        /// <returns>The key.</returns>
        public static string RangeLabelKey(UsageRange range)
        {
            return "usage.range." + UsageWindow.KeyOf(range);
        }

        /// <summary>The localization key of a range's button tooltip, for example "usage.range.quarter.tip".</summary>
        /// <param name="range">The range.</param>
        /// <returns>The key.</returns>
        public static string RangeTipKey(UsageRange range)
        {
            return RangeLabelKey(range) + ".tip";
        }

        /// <summary>The localization key of a dimension's picker label, for example "usage.dim.callkind".</summary>
        /// <param name="dimension">The dimension key; unknown or blank values map to model.</param>
        /// <returns>The key.</returns>
        public static string DimensionLabelKey(string? dimension)
        {
            return "usage.dim." + NormalizeDimension(dimension);
        }

        /// <summary>Normalize a dimension key to one of <see cref="Dimensions"/> (model when unknown).</summary>
        /// <param name="dimension">The dimension key.</param>
        /// <returns>The normalized key.</returns>
        public static string NormalizeDimension(string? dimension)
        {
            string key = (dimension ?? string.Empty).Trim().ToLowerInvariant();
            return Dimensions.Contains(key) ? key : "model";
        }

        /// <summary>
        /// The bucket width of a series: the gap between the first two bucket starts, else the window's bucket
        /// size, else the window's span (a single bucket).
        /// </summary>
        /// <param name="buckets">The buckets, ascending.</param>
        /// <param name="window">The resolved window, or null.</param>
        /// <returns>The width in milliseconds (at least 1).</returns>
        public static long BucketWidthMs(IReadOnlyList<UsageBucket>? buckets, UsageWindow? window)
        {
            if (buckets != null && buckets.Count >= 2)
            {
                long gap = buckets[1].BucketStartUnixMs - buckets[0].BucketStartUnixMs;
                if (gap > 0)
                {
                    return gap;
                }
            }

            if (window != null)
            {
                if (window.BucketMs > 0)
                {
                    return window.BucketMs;
                }

                if (window.ToUnixMs > window.FromUnixMs)
                {
                    return window.ToUnixMs - window.FromUnixMs;
                }
            }

            return 1;
        }

        /// <summary>
        /// The inclusive call window of a bucket for drill-down: <c>[start, start + width - 1]</c>.
        /// </summary>
        /// <param name="bucketStartUnixMs">The bucket start.</param>
        /// <param name="widthMs">The bucket width (floored at 1).</param>
        /// <returns>The inclusive bounds, Unix epoch milliseconds.</returns>
        public static (long FromUnixMs, long ToUnixMs) DrillWindow(long bucketStartUnixMs, long widthMs)
        {
            long width = Math.Max(1, widthMs);
            return (bucketStartUnixMs, bucketStartUnixMs + width - 1);
        }

        /// <summary>The Calls chart stack for a bucket: succeeded (<c>Calls - Errors</c>, floored at 0) and failed.</summary>
        /// <param name="metrics">The bucket metrics.</param>
        /// <returns>The two segments.</returns>
        public static (long Succeeded, long Failed) CallStack(UsageMetrics? metrics)
        {
            if (metrics == null)
            {
                return (0, 0);
            }

            long failed = Math.Max(0, metrics.Errors);
            return (Math.Max(0, metrics.Calls - failed), failed);
        }

        /// <summary>
        /// The Tokens chart stack for a bucket: uncached input (<c>Input - Cached</c>, floored at 0), cached, and
        /// output, plus their total.
        /// </summary>
        /// <param name="metrics">The bucket metrics.</param>
        /// <returns>The segments and total.</returns>
        public static (long Uncached, long Cached, long Output, long Total) TokenStack(UsageMetrics? metrics)
        {
            if (metrics == null)
            {
                return (0, 0, 0, 0);
            }

            long uncached = Math.Max(0, metrics.InputTokens - metrics.CachedTokens);
            long cached = Math.Max(0, metrics.CachedTokens);
            long output = Math.Max(0, metrics.OutputTokens);
            return (uncached, cached, output, uncached + cached + output);
        }

        /// <summary>
        /// The failure-reason list: blank codes become "unknown", duplicates combine, most frequent first (ties by code),
        /// zero counts dropped.
        /// </summary>
        /// <param name="errors">The error counts from the errors query.</param>
        /// <returns>The reasons.</returns>
        public static List<UsageErrorCount> FailureReasons(IEnumerable<UsageErrorCount>? errors)
        {
            return (errors ?? Enumerable.Empty<UsageErrorCount>())
                .Where(e => e != null && e.Count > 0)
                .GroupBy(e => string.IsNullOrWhiteSpace(e.ErrorCode) ? "unknown" : e.ErrorCode.Trim(), StringComparer.Ordinal)
                .Select(g => new UsageErrorCount { ErrorCode = g.Key, Count = g.Sum(e => e.Count) })
                .OrderByDescending(e => e.Count)
                .ThenBy(e => e.ErrorCode, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Orders split series for stacking: named series in the query's rank order (at most <paramref name="top"/>),
        /// then "Other" last. Each named series gets its rank as a stable palette index; "Other" gets -1 (neutral grey).
        /// </summary>
        /// <param name="series">The split series.</param>
        /// <param name="top">How many named series to keep; extra named series are folded into "Other".</param>
        /// <returns>The ordered series with their color indexes.</returns>
        public static List<(UsageSeries Series, int ColorIndex)> SplitStack(IReadOnlyList<UsageSeries>? series, int top = SplitTop)
        {
            List<(UsageSeries, int)> result = new List<(UsageSeries, int)>();
            if (series == null)
            {
                return result;
            }

            int keep = Math.Max(1, top);
            List<UsageSeries> named = series.Where(s => s != null && !s.IsOther).ToList();
            List<UsageSeries> others = series.Where(s => s != null && s.IsOther).ToList();
            if (named.Count > keep)
            {
                others.AddRange(named.Skip(keep));
                named = named.Take(keep).ToList();
            }

            for (int i = 0; i < named.Count; i++)
            {
                result.Add((named[i], i));
            }

            if (others.Count == 1)
            {
                result.Add((others[0], -1));
            }
            else if (others.Count > 1)
            {
                result.Add((new UsageSeries { Key = "Other", IsOther = true, Buckets = UsageMerge.MergeBuckets(others.Select(o => o.Buckets)) }, -1));
            }

            return result;
        }

        /// <summary>The tallest stacked column across split series: the maximum per-bucket sum of total tokens.</summary>
        /// <param name="series">The split series (sharing one bucket grid).</param>
        /// <returns>The maximum (0 when empty).</returns>
        public static long SplitMax(IEnumerable<UsageSeries>? series)
        {
            Dictionary<long, long> sums = new Dictionary<long, long>();
            foreach (UsageSeries s in series ?? Enumerable.Empty<UsageSeries>())
            {
                foreach (UsageBucket b in s.Buckets)
                {
                    sums.TryGetValue(b.BucketStartUnixMs, out long current);
                    sums[b.BucketStartUnixMs] = current + Math.Max(0, b.Metrics.TotalTokens);
                }
            }

            return sums.Count == 0 ? 0 : sums.Values.Max();
        }

        /// <summary>
        /// The "Usage by" rows: sorted by cost then total tokens (both descending, ties by value), rows without any
        /// calls or tokens dropped, at most <paramref name="max"/>.
        /// </summary>
        /// <param name="rows">The breakdown rows.</param>
        /// <param name="max">The row limit.</param>
        /// <returns>The rows to draw.</returns>
        public static List<UsageBreakdownRow> TopBreakdown(IEnumerable<UsageBreakdownRow>? rows, int max = BreakdownRows)
        {
            return (rows ?? Enumerable.Empty<UsageBreakdownRow>())
                .Where(r => r != null && (r.Metrics.Calls > 0 || r.Metrics.TotalTokens > 0))
                .OrderByDescending(r => r.Metrics.CostUsd)
                .ThenByDescending(r => r.Metrics.TotalTokens)
                .ThenBy(r => r.Value, StringComparer.Ordinal)
                .Take(Math.Max(1, max))
                .ToList();
        }

        /// <summary>The split metrics in picker order: tokens (the default), calls, cost (see <see cref="UsageCsv.Metrics"/>).</summary>
        public static IReadOnlyList<string> SplitMetrics
        {
            get => UsageCsv.Metrics;
        }

        /// <summary>The bucket count up to which lines mode draws a point marker at every bucket.</summary>
        public const int LineMarkerMaxBuckets = 24;

        /// <summary>The localization key of a split metric's label (the matching chart tab name: Tokens, Calls, or Cost).</summary>
        /// <param name="metric">The metric key, or null (tokens).</param>
        /// <returns>The key.</returns>
        public static string SplitMetricLabelKey(string? metric)
        {
            return "usage.metric." + UsageCsv.NormalizeMetric(metric);
        }

        /// <summary>The value a split metric reads from a bucket or row: total tokens, calls, or cost in USD.</summary>
        /// <param name="metrics">The metrics, or null (0).</param>
        /// <param name="metric">The metric key.</param>
        /// <returns>The value.</returns>
        public static double SplitValue(UsageMetrics? metrics, string? metric)
        {
            return UsageCsv.MetricValue(metrics, metric);
        }

        /// <summary>The tallest stacked column across split series for a metric: the maximum per-bucket sum.</summary>
        /// <param name="series">The split series (sharing one bucket grid).</param>
        /// <param name="metric">The metric key.</param>
        /// <returns>The maximum (0 when empty).</returns>
        public static double SplitMax(IEnumerable<UsageSeries>? series, string? metric)
        {
            Dictionary<long, double> sums = new Dictionary<long, double>();
            foreach (UsageSeries s in series ?? Enumerable.Empty<UsageSeries>())
            {
                foreach (UsageBucket b in s.Buckets)
                {
                    sums.TryGetValue(b.BucketStartUnixMs, out double current);
                    sums[b.BucketStartUnixMs] = current + Math.Max(0, SplitValue(b.Metrics, metric));
                }
            }

            return sums.Count == 0 ? 0 : sums.Values.Max();
        }

        /// <summary>
        /// A split series' metric per chart bucket, aligned by bucket start (a bucket the series lacks is 0).
        /// </summary>
        /// <param name="series">The series, or null (all zeros).</param>
        /// <param name="buckets">The chart's buckets, oldest first, or null.</param>
        /// <param name="metric">The metric key.</param>
        /// <returns>One value per chart bucket.</returns>
        public static double[] AlignSeries(UsageSeries? series, IReadOnlyList<UsageBucket>? buckets, string? metric)
        {
            int count = buckets?.Count ?? 0;
            double[] values = new double[count];
            if (series == null || buckets == null) return values;
            Dictionary<long, int> index = new Dictionary<long, int>();
            for (int i = 0; i < count; i++) index[buckets[i].BucketStartUnixMs] = i;
            foreach (UsageBucket b in series.Buckets)
            {
                if (b != null && index.TryGetValue(b.BucketStartUnixMs, out int at)) values[at] += SplitValue(b.Metrics, metric);
            }

            return values;
        }

        /// <summary>
        /// The "Usage by" rows ranked by a split metric (descending; ties by tokens, then cost, then value), rows
        /// without any calls or tokens dropped, at most <paramref name="max"/>.
        /// </summary>
        /// <param name="rows">The breakdown rows.</param>
        /// <param name="metric">The metric key ("tokens", "calls", or "cost").</param>
        /// <param name="max">The row limit; 0 or less keeps every row.</param>
        /// <returns>The rows to draw or export.</returns>
        public static List<UsageBreakdownRow> RankBreakdown(IEnumerable<UsageBreakdownRow>? rows, string? metric, int max = BreakdownRows)
        {
            IEnumerable<UsageBreakdownRow> ranked = (rows ?? Enumerable.Empty<UsageBreakdownRow>())
                .Where(r => r != null && (r.Metrics.Calls > 0 || r.Metrics.TotalTokens > 0))
                .OrderByDescending(r => SplitValue(r.Metrics, metric))
                .ThenByDescending(r => r.Metrics.TotalTokens)
                .ThenByDescending(r => r.Metrics.CostUsd)
                .ThenBy(r => r.Value, StringComparer.Ordinal);
            return (max > 0 ? ranked.Take(max) : ranked).ToList();
        }

        /// <summary>The lines-mode scale: the largest single value of any series (lines are not stacked).</summary>
        /// <param name="series">The series values.</param>
        /// <returns>The maximum (0 when empty).</returns>
        public static double LineMax(IEnumerable<IReadOnlyList<double>>? series)
        {
            double max = 0;
            foreach (IReadOnlyList<double> values in series ?? Enumerable.Empty<IReadOnlyList<double>>())
            {
                foreach (double v in values)
                {
                    if (v > max) max = v;
                }
            }

            return max;
        }

        /// <summary>
        /// Lines-mode points for one series in a plot of the given size: each bucket's point is centered in its
        /// equal-width column (x = (i + 0.5) * width / n) at its value's height from the bottom, clamped to the plot.
        /// </summary>
        /// <param name="values">One value per bucket.</param>
        /// <param name="width">The plot width.</param>
        /// <param name="height">The plot height.</param>
        /// <param name="max">The scale maximum; 0 or less puts every point on the baseline.</param>
        /// <returns>The points, left to right.</returns>
        public static List<(double X, double Y)> LinePoints(IReadOnlyList<double>? values, double width, double height, double max)
        {
            List<(double, double)> points = new List<(double, double)>();
            int n = values?.Count ?? 0;
            for (int i = 0; i < n; i++)
            {
                double fraction = max > 0 ? Math.Clamp(values![i] / max, 0, 1) : 0;
                points.Add(((i + 0.5) * width / n, height - (fraction * height)));
            }

            return points;
        }

        /// <summary>Whether lines mode marks every bucket's point (only with few buckets, so markers never crowd).</summary>
        /// <param name="buckets">The bucket count.</param>
        /// <returns>True for at most <see cref="LineMarkerMaxBuckets"/> buckets.</returns>
        public static bool ShowLineMarkers(int buckets)
        {
            return buckets > 0 && buckets <= LineMarkerMaxBuckets;
        }

        /// <summary>Whether any bucket recorded a call.</summary>
        /// <param name="buckets">The buckets.</param>
        /// <returns>True when there is usage to chart.</returns>
        public static bool HasUsage(IEnumerable<UsageBucket>? buckets)
        {
            return (buckets ?? Enumerable.Empty<UsageBucket>()).Any(b => b.Metrics.Calls > 0 || b.Metrics.TotalTokens > 0);
        }

        /// <summary>
        /// The heatmap fill for shade levels 1 to 4 (low to high) on a light surface: one green hue, light to dark,
        /// validated for monotone lightness, visible steps, and at least 2:1 contrast for the lightest step. Level 0
        /// (no usage) is <see cref="HeatmapEmptyLight"/>.
        /// </summary>
        public static IReadOnlyList<string> HeatmapRampLight { get; } = new[] { "#6cc596", "#35a571", "#17784f", "#0a4d33" };

        /// <summary>
        /// The heatmap fill for shade levels 1 to 4 (low to high) on a dark surface: the same green hue, dark to
        /// bright, with at least 2:1 contrast for the darkest step against the dark surface. Level 0 is
        /// <see cref="HeatmapEmptyDark"/>.
        /// </summary>
        public static IReadOnlyList<string> HeatmapRampDark { get; } = new[] { "#1f6b48", "#2f9a66", "#4fc98a", "#9be8bd" };

        /// <summary>
        /// The no-usage fill on a light surface: a neutral light grey (OKLab lightness 0.926), clearly lighter than the
        /// first green step (0.755), so an empty cell never reads as white or as a faint shade.
        /// </summary>
        public const string HeatmapEmptyLight = "#e3e7eb";

        /// <summary>
        /// The no-usage fill on a dark surface: a neutral dark grey (OKLab lightness 0.330), clearly darker than the
        /// first green step (0.472).
        /// </summary>
        public const string HeatmapEmptyDark = "#2e3640";

        /// <summary>The fill for a heatmap shade level: the neutral grey for level 0 (no usage), else a ramp step.</summary>
        /// <param name="level">The level from <see cref="UsageHeatmapScale.LevelOf"/>.</param>
        /// <param name="dark">True for the dark theme.</param>
        /// <returns>The hex color.</returns>
        public static string HeatmapFill(int level, bool dark)
        {
            if (level <= 0)
            {
                return dark ? HeatmapEmptyDark : HeatmapEmptyLight;
            }

            IReadOnlyList<string> ramp = dark ? HeatmapRampDark : HeatmapRampLight;
            return ramp[Math.Min(level, ramp.Count) - 1];
        }

        /// <summary>The shared catalog key naming the heatmap's x-axis ("us.hm.x.1h" = "hour of day").</summary>
        /// <param name="columnUnit">The layout's <see cref="UsageHeatmapLayout.ColumnUnit"/>.</param>
        /// <returns>The key.</returns>
        public static string HeatmapColumnKey(string? columnUnit)
        {
            switch (columnUnit)
            {
                case "5m":
                case "1h":
                case "1d":
                case "halfday":
                case "1w":
                case "month":
                    return "us.hm.x." + columnUnit;
                default:
                    return "us.hm.x.period";
            }
        }

        /// <summary>The shared catalog key naming the heatmap's y-axis ("us.hm.y.minuteofhour" = "minute of the hour").</summary>
        /// <param name="rowKind">The layout's <see cref="UsageHeatmapLayout.RowKind"/>.</param>
        /// <returns>The key.</returns>
        public static string HeatmapRowKey(string? rowKind)
        {
            switch (rowKind)
            {
                case UsageHeatmapLayout.RowKindMinute:
                case UsageHeatmapLayout.RowKindMinuteOfHour:
                case UsageHeatmapLayout.RowKindHours:
                case UsageHeatmapLayout.RowKindClock:
                case UsageHeatmapLayout.RowKindWeekday:
                case UsageHeatmapLayout.RowKindWeek:
                    return "us.hm.y." + rowKind;
                default:
                    return "us.hm.y.weeks";
            }
        }

        /// <summary>
        /// The axis caption ("Columns: hour of day · Rows: minute of the hour") from the shared catalog's
        /// <c>us.hm.axes</c> template.
        /// </summary>
        /// <param name="layout">The layout. Required.</param>
        /// <param name="translate">Resolves a catalog key. Required.</param>
        /// <returns>The caption.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string HeatmapCaption(UsageHeatmapLayout layout, Func<string, string> translate)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (translate == null) throw new ArgumentNullException(nameof(translate));
            return translate("us.hm.axes")
                .Replace("{cols}", translate(HeatmapColumnKey(layout.ColumnUnit)), StringComparison.Ordinal)
                .Replace("{rows}", translate(HeatmapRowKey(layout.RowKind)), StringComparison.Ordinal);
        }

        /// <summary>
        /// The pixel height of one heatmap row, including the gap: about square with a column of a 900 pixel plot,
        /// and short enough that every row fits in 260 pixels, clamped to 14..30 so a wide grid stays wide.
        /// </summary>
        /// <param name="rows">The row count.</param>
        /// <param name="columns">The column count.</param>
        /// <returns>The row height in pixels.</returns>
        public static double HeatmapRowHeight(int rows, int columns)
        {
            double square = Math.Floor(900.0 / Math.Max(1, columns));
            double fit = Math.Floor(260.0 / Math.Max(1, rows));
            return Math.Clamp(Math.Min(square, fit), 14, 30);
        }

        /// <summary>
        /// Which rows get a label: all of them when rows are at least 14 pixels tall, otherwise every 2nd (the first
        /// and last always labeled) so 11 pixel text never overlaps.
        /// </summary>
        /// <param name="rows">The row count.</param>
        /// <param name="rowHeight">The row height in pixels.</param>
        /// <returns>The labeled row indices.</returns>
        public static List<int> HeatmapLabeledRows(int rows, double rowHeight)
        {
            int fit = rowHeight >= 14 ? rows : (int)Math.Floor(rows * rowHeight / 14.0);
            return UsageHeatmapLayout.LabeledIndices(rows, Math.Max(2, fit), true);
        }

        /// <summary>
        /// The columns one x-axis label needs on a plot: its width plus 6 pixels of spacing over the column pitch,
        /// at least 1. Pass it to <see cref="UsageHeatmapLayout.LabeledColumns"/>.
        /// </summary>
        /// <param name="labelWidth">The widest label in pixels.</param>
        /// <param name="plotWidth">The plot width in pixels.</param>
        /// <param name="columns">The column count.</param>
        /// <returns>The minimum gap in columns.</returns>
        public static int HeatmapLabelGap(double labelWidth, double plotWidth, int columns)
        {
            double pitch = plotWidth / Math.Max(1, columns);
            if (pitch <= 0) return Math.Max(1, columns);
            return Math.Max(1, (int)Math.Ceiling((labelWidth + 6) / pitch));
        }

        /// <summary>
        /// The x-axis label format for a range: times for hour and day, weekday and time for week, dates for month
        /// and quarter, dates with the year for year, and for all a date with time when buckets are under a day
        /// (a young store) or a date with the year otherwise.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="bucketMs">The bucket width.</param>
        /// <returns>A .NET date format string.</returns>
        public static string AxisFormat(UsageRange range, long bucketMs)
        {
            switch (range)
            {
                case UsageRange.Hour:
                case UsageRange.Day:
                    return "HH:mm";
                case UsageRange.Week:
                    return "ddd HH:mm";
                case UsageRange.Month:
                case UsageRange.Quarter:
                    return "MMM d";
                case UsageRange.Year:
                    return "MMM d, yyyy";
                case UsageRange.All:
                    return bucketMs < 86_400_000L ? "MMM d HH:mm" : "MMM d, yyyy";
                default:
                    return "HH:mm";
            }
        }

        /// <summary>
        /// A bucket's time window as text, for tooltips and the drill-down indicator: "MMM d HH:mm–HH:mm" for
        /// buckets under a day, "MMM d, yyyy" for a one-day bucket, and "MMM d – MMM d, yyyy" for longer buckets.
        /// </summary>
        /// <param name="bucketStartUnixMs">The bucket start.</param>
        /// <param name="widthMs">The bucket width.</param>
        /// <param name="zone">The time zone to show; null uses local time.</param>
        /// <param name="culture">The culture for month and day names; null uses the current culture.</param>
        /// <returns>The window text.</returns>
        public static string FormatBucketWindow(long bucketStartUnixMs, long widthMs, TimeZoneInfo? zone = null, CultureInfo? culture = null)
        {
            TimeZoneInfo tz = zone ?? TimeZoneInfo.Local;
            CultureInfo ci = culture ?? CultureInfo.CurrentCulture;
            long width = Math.Max(1, widthMs);
            DateTime start = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bucketStartUnixMs), tz).DateTime;
            DateTime end = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bucketStartUnixMs + width), tz).DateTime;
            const long day = 86_400_000L;

            if (width < day)
            {
                return start.ToString("MMM d HH:mm", ci) + "–" + end.ToString("HH:mm", ci);
            }

            if (width == day)
            {
                return start.ToString("MMM d, yyyy", ci);
            }

            DateTime last = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bucketStartUnixMs + width - 1), tz).DateTime;
            return start.ToString("MMM d", ci) + " – " + last.ToString("MMM d, yyyy", ci);
        }

        #endregion
    }
}
