namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using Mux.Core.Sessions;
    using Mux.Core.Telemetry;

    /// <summary>
    /// The in-process data providers behind <see cref="UsageChartsModal"/>: builds <see cref="UsageChartData"/>,
    /// <see cref="UsageDimensionData"/>, and drill-down call pages from a <see cref="UsageQueryService"/>, applying the
    /// <c>/usage</c> label or tag filter to every query. The static shaping helpers turn query results into chart data
    /// without any I/O so they can be tested directly.
    /// </summary>
    public sealed class UsageChartQueries
    {
        #region Public-Members

        /// <summary>The series kept per split before the rest fold into "Other".</summary>
        public const int SplitTop = 6;

        /// <summary>The most rows the "Usage by" page shows.</summary>
        public const int BreakdownRows = 10;

        /// <summary>The most calls a bucket drill-down loads.</summary>
        public const int DrillPageSize = 200;

        #endregion

        #region Private-Members

        private const long DayMs = 24L * 60L * 60L * 1000L;

        private readonly UsageQueryService _Query;
        private readonly List<string>? _Labels;
        private readonly List<SessionTag>? _Tags;
        private readonly Func<long> _Now;
        private readonly TimeZoneInfo _Zone;
        private readonly CancellationToken _Token;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageChartQueries"/> class.
        /// </summary>
        /// <param name="query">The usage query service. Required.</param>
        /// <param name="labels">The session labels every query is scoped to, or null for none.</param>
        /// <param name="tags">The session tags every query is scoped to, or null for none.</param>
        /// <param name="token">A token that cancels the queries.</param>
        /// <param name="now">Supplies "now" in Unix epoch milliseconds; null uses the system clock.</param>
        /// <param name="zone">The time zone the heatmap rows align to; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public UsageChartQueries(UsageQueryService query, List<string>? labels, List<SessionTag>? tags, CancellationToken token, Func<long>? now = null, TimeZoneInfo? zone = null)
        {
            _Zone = zone ?? TimeZoneInfo.Local;
            _Query = query ?? throw new ArgumentNullException(nameof(query));
            _Labels = labels != null && labels.Count > 0 ? labels : null;
            _Tags = tags != null && tags.Count > 0 ? tags : null;
            _Token = token;
            _Now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Loads the chart data for a range: KPIs, the dense time series, failure reasons, and the heatmap.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <returns>The chart data.</returns>
        public UsageChartData Load(UsageRange range)
        {
            long now = _Now();
            UsageFilter scope = NewFilter(0, 0);
            UsageWindow window = _Query.ResolveWindowAsync(range, scope, now, _Token).GetAwaiter().GetResult();
            UsageFilter filter = NewFilter(window.FromUnixMs, window.ToUnixMs - 1);

            UsageSummary summary = _Query.GetSummaryAsync(filter, _Token).GetAwaiter().GetResult();
            List<UsageBucket> series = _Query.GetTimeseriesAsync(filter, window.BucketMs, _Token).GetAwaiter().GetResult();
            List<UsageErrorCount> errors = _Query.GetErrorCountsAsync(filter, _Token).GetAwaiter().GetResult();

            long earliest = range == UsageRange.All ? _Query.GetEarliestEventUnixMsAsync(scope, _Token).GetAwaiter().GetResult() : 0;
            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(range, now, _Zone, earliest);
            UsageHeatmap heatmap = _Query.GetHeatmapAsync(layout, scope, _Token).GetAwaiter().GetResult();

            return BuildChartData(range, window, summary.Metrics, DescribeScope(_Labels, _Tags), series, errors, heatmap);
        }

        /// <summary>
        /// Loads the split series and whole-range rows for a dimension over the window of loaded chart data.
        /// </summary>
        /// <param name="data">The loaded chart data whose window and bucket width to use. Required.</param>
        /// <param name="dimension">"model", "endpoint", "provider", or "callkind".</param>
        /// <returns>The dimension data.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="data"/> is null.</exception>
        public UsageDimensionData LoadDimension(UsageChartData data, string dimension)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            UsageFilter filter = NewFilter(data.WindowFromUnixMs, data.WindowToUnixMs > 0 ? data.WindowToUnixMs - 1 : 0);
            long bucketMs = data.BucketMs > 0 ? data.BucketMs : 15L * 60L * 1000L;
            List<UsageSeries> series = _Query.GetSplitTimeseriesAsync(dimension, filter, bucketMs, SplitTop, _Token).GetAwaiter().GetResult();
            List<UsageBreakdownRow> rows = _Query.GetBreakdownAsync(dimension, filter, _Token).GetAwaiter().GetResult();
            return BuildDimensionData(dimension, series, rows);
        }

        /// <summary>
        /// Loads the calls in an inclusive time window, newest first, with the same label or tag filter.
        /// </summary>
        /// <param name="fromUnixMs">The inclusive lower bound, Unix epoch milliseconds.</param>
        /// <param name="toUnixMs">The inclusive upper bound, Unix epoch milliseconds.</param>
        /// <returns>The first page of calls (up to <see cref="DrillPageSize"/>) and the total count.</returns>
        public UsageEventPage LoadCalls(long fromUnixMs, long toUnixMs)
        {
            return _Query.GetEventsAsync(NewFilter(fromUnixMs, toUnixMs), 1, DrillPageSize, _Token).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Shapes query results into chart data: header KPIs, bucket labels and metrics, failure reasons, and the
        /// clock-aligned heatmap. Performs no I/O.
        /// </summary>
        /// <param name="range">The range (chooses label formats).</param>
        /// <param name="window">The resolved chart window.</param>
        /// <param name="totals">The range totals for the KPI line, or null.</param>
        /// <param name="scope">A filter description such as "label:wip", or empty.</param>
        /// <param name="series">The dense time series for <paramref name="window"/>, or null.</param>
        /// <param name="errors">The error counts, most frequent first, or null.</param>
        /// <param name="heatmap">The clock-aligned heatmap, or null for none.</param>
        /// <returns>The chart data.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="window"/> is null.</exception>
        public static UsageChartData BuildChartData(
            UsageRange range,
            UsageWindow window,
            UsageMetrics? totals,
            string? scope,
            List<UsageBucket>? series,
            List<UsageErrorCount>? errors,
            UsageHeatmap? heatmap)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));

            UsageChartData data = new UsageChartData
            {
                WindowFromUnixMs = window.FromUnixMs,
                WindowToUnixMs = window.ToUnixMs,
                BucketMs = window.BucketMs
            };

            UsageMetrics kpi = totals ?? new UsageMetrics();
            string line = KpiLine(kpi);
            data.HeaderLines.Add(string.IsNullOrEmpty(scope) ? line : scope + " — " + line);

            if (series != null)
            {
                foreach (UsageBucket bucket in series)
                {
                    data.BucketStartsUnixMs.Add(bucket.BucketStartUnixMs);
                    data.BucketLabels.Add(FormatBucketLabel(bucket.BucketStartUnixMs, range, window.BucketMs));
                    data.Buckets.Add(bucket.Metrics ?? new UsageMetrics());
                }
            }

            if (errors != null)
            {
                foreach (UsageErrorCount error in errors)
                {
                    if (error != null && error.Count > 0) data.ErrorCounts.Add(error);
                }

                data.ErrorCounts.Sort((a, b) => b.Count.CompareTo(a.Count));
            }

            data.Heatmap = heatmap;
            return data;
        }

        /// <summary>
        /// Shapes split and breakdown results into dimension data: keeps the series as ranked (top values, "Other"
        /// last) and the breakdown sorted by cost then tokens, at most <see cref="BreakdownRows"/> rows. Performs no I/O.
        /// </summary>
        /// <param name="dimension">The dimension key.</param>
        /// <param name="series">The split series, or null.</param>
        /// <param name="rows">The breakdown rows, or null.</param>
        /// <returns>The dimension data.</returns>
        public static UsageDimensionData BuildDimensionData(string dimension, List<UsageSeries>? series, List<UsageBreakdownRow>? rows)
        {
            UsageDimensionData data = new UsageDimensionData { Dimension = string.IsNullOrWhiteSpace(dimension) ? "model" : dimension };
            if (series != null)
            {
                foreach (UsageSeries s in series)
                {
                    if (s != null && !s.IsOther) data.Series.Add(s);
                }

                foreach (UsageSeries s in series)
                {
                    if (s != null && s.IsOther) data.Series.Add(s);
                }
            }

            if (rows != null)
            {
                List<UsageBreakdownRow> sorted = new List<UsageBreakdownRow>();
                foreach (UsageBreakdownRow row in rows)
                {
                    if (row != null) sorted.Add(row);
                }

                sorted.Sort((a, b) =>
                {
                    int byCost = b.Metrics.CostUsd.CompareTo(a.Metrics.CostUsd);
                    return byCost != 0 ? byCost : b.Metrics.TotalTokens.CompareTo(a.Metrics.TotalTokens);
                });
                for (int i = 0; i < sorted.Count && i < BreakdownRows; i++) data.Rows.Add(sorted[i]);
                data.AllRows.AddRange(sorted);
            }

            return data;
        }

        /// <summary>
        /// Ranks whole-range rows by a split metric ("tokens", "calls", or "cost"; see <see cref="UsageCsv.Metrics"/>),
        /// largest first, breaking ties by tokens, then cost, then value, and keeps at most <paramref name="max"/> rows.
        /// Performs no I/O.
        /// </summary>
        /// <param name="rows">The rows, or null.</param>
        /// <param name="metric">The metric key.</param>
        /// <param name="max">The most rows to keep; 0 or less keeps every row.</param>
        /// <returns>The ranked rows.</returns>
        public static List<UsageBreakdownRow> RankRows(IEnumerable<UsageBreakdownRow>? rows, string? metric, int max = BreakdownRows)
        {
            List<UsageBreakdownRow> sorted = new List<UsageBreakdownRow>();
            if (rows != null)
            {
                foreach (UsageBreakdownRow row in rows)
                {
                    if (row != null) sorted.Add(row);
                }
            }

            string key = UsageCsv.NormalizeMetric(metric);
            sorted.Sort((a, b) =>
            {
                int byMetric = UsageCsv.MetricValue(b.Metrics, key).CompareTo(UsageCsv.MetricValue(a.Metrics, key));
                if (byMetric != 0) return byMetric;
                int byTokens = (b.Metrics?.TotalTokens ?? 0).CompareTo(a.Metrics?.TotalTokens ?? 0);
                if (byTokens != 0) return byTokens;
                int byCost = (b.Metrics?.CostUsd ?? 0).CompareTo(a.Metrics?.CostUsd ?? 0);
                return byCost != 0 ? byCost : string.CompareOrdinal(a.Value, b.Value);
            });
            if (max > 0 && sorted.Count > max) sorted.RemoveRange(max, sorted.Count - max);
            return sorted;
        }

        /// <summary>
        /// Formats a bucket start (shown in local time) for the chart's X axis: times for hour and day, weekday for
        /// week, month/day for month and quarter, and full dates for year and all time (all time keeps the time of day
        /// when its buckets are shorter than a day).
        /// </summary>
        /// <param name="unixMs">The bucket start, Unix epoch milliseconds.</param>
        /// <param name="range">The range.</param>
        /// <param name="bucketMs">The bucket width in milliseconds (used by <see cref="UsageRange.All"/>).</param>
        /// <returns>The label.</returns>
        public static string FormatBucketLabel(long unixMs, UsageRange range, long bucketMs)
        {
            DateTimeOffset t = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();
            switch (range)
            {
                case UsageRange.Hour:
                case UsageRange.Day:
                    return t.ToString("HH:mm", CultureInfo.InvariantCulture);
                case UsageRange.Week:
                    return t.ToString("ddd HH:mm", CultureInfo.InvariantCulture);
                case UsageRange.Month:
                case UsageRange.Quarter:
                    return t.ToString("MM/dd", CultureInfo.InvariantCulture);
                case UsageRange.Year:
                    return t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                default:
                    return bucketMs > 0 && bucketMs < DayMs
                        ? t.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture)
                        : t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Describes a label or tag scope for the header, such as "label:wip" or "tag:env:prod"; empty for none.
        /// </summary>
        /// <param name="labels">The labels, or null.</param>
        /// <param name="tags">The tags, or null.</param>
        /// <returns>The description.</returns>
        public static string DescribeScope(List<string>? labels, List<SessionTag>? tags)
        {
            if (labels != null && labels.Count > 0)
            {
                return "label:" + string.Join(",", labels);
            }

            if (tags != null && tags.Count > 0)
            {
                List<string> parts = new List<string>();
                foreach (SessionTag tag in tags) parts.Add(tag.Key + ":" + tag.Value);
                return "tag:" + string.Join(",", parts);
            }

            return string.Empty;
        }

        /// <summary>
        /// The header KPI line: tokens, cost, calls, errors, and error rate.
        /// </summary>
        /// <param name="m">The totals. Required.</param>
        /// <returns>The line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="m"/> is null.</exception>
        public static string KpiLine(UsageMetrics m)
        {
            if (m == null) throw new ArgumentNullException(nameof(m));
            double rate = m.Calls > 0 ? (double)m.Errors / m.Calls : 0;
            return UsageChartFormat.Tokens(m.TotalTokens) + " tok · " + UsageChartFormat.Usd(m.CostUsd) + " · " + m.Calls + " calls · "
                + m.Errors + " err (" + UsageChartFormat.Percent(rate) + ")";
        }

        #endregion

        #region Private-Methods

        private UsageFilter NewFilter(long fromUnixMs, long toUnixMs)
        {
            return new UsageFilter
            {
                FromUnixMs = fromUnixMs,
                ToUnixMs = toUnixMs,
                Labels = _Labels,
                Tags = _Tags
            };
        }

        #endregion
    }
}
