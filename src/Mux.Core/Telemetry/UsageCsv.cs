namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// The one CSV format every usage chart exports, used by the terminal and the desktop app (the web dashboard and
    /// VS Code mirror it in JavaScript and are tested to produce identical text). The format:
    /// <list type="bullet">
    /// <item>A header row, then one row per bucket (oldest first), per value for "Usage by", or per cell for the
    /// heatmap. Every line, the last included, ends with <see cref="Newline"/> (a single <c>\n</c>). No byte order
    /// mark.</item>
    /// <item>Over-time charts start with <c>bucket_start</c> and <c>bucket_end</c> (end = start + bucket width, so
    /// exclusive); the heatmap starts with <c>cell_start</c> and <c>cell_end</c>. Times are ISO 8601 local time with
    /// the offset in effect at that instant, to the second: <c>2026-11-09T14:15:00-08:00</c> (see
    /// <see cref="FormatTime"/>).</item>
    /// <item>Numbers are invariant (see <see cref="FormatNumber(double)"/>): <c>.</c> decimal, no thousands separators, no
    /// exponent, at most six decimals with trailing zeros trimmed. A missing value is an empty cell.</item>
    /// <item>A cell that a spreadsheet would run as a formula (starting with <c>=</c>, <c>+</c>, <c>@</c>, a tab, CR, or a
    /// <c>-</c> that does not begin a number) gets a leading <c>'</c>. A cell is quoted only when it contains a comma, a
    /// double quote, CR, or LF, or starts or ends with a space or tab; quotes inside are doubled (see
    /// <see cref="Escape"/>).</item>
    /// </list>
    /// Chart columns: calls <c>succeeded,failed</c>; tokens <c>uncached_input,cached_input,output</c>; cost
    /// <c>cost_usd</c>; the four box plots <c>min,avg,p95,p99,max,count</c> (stats empty when count is 0);
    /// "&lt;Metric&gt; by &lt;dimension&gt;" one column per series named by its value (an empty value is
    /// <c>unknown</c>), with <c>Other</c> last; "Usage by" (a blank value is also <c>unknown</c>) <c>&lt;dimension&gt;,calls,failed,uncached_input,cached_input,output,total_tokens,cost_usd</c>
    /// with the whole window as its bucket; heatmap <c>tokens,calls</c> for every cell inside the window, oldest first.
    /// File names come from <see cref="FileName"/>.
    /// </summary>
    public static class UsageCsv
    {
        #region Public-Members

        /// <summary>The line terminator after every row, the last included.</summary>
        public const string Newline = "\n";

        /// <summary>The bucket start column.</summary>
        public const string BucketStart = "bucket_start";

        /// <summary>The bucket end column (exclusive).</summary>
        public const string BucketEnd = "bucket_end";

        /// <summary>The heatmap cell start column.</summary>
        public const string CellStart = "cell_start";

        /// <summary>The heatmap cell end column (exclusive).</summary>
        public const string CellEnd = "cell_end";

        /// <summary>The header of the folded remainder series in a split export.</summary>
        public const string OtherHeader = "Other";

        /// <summary>The header of a split series whose value is empty.</summary>
        public const string UnknownHeader = "unknown";

        /// <summary>Chart slug: calls over time.</summary>
        public const string ChartCalls = "calls";

        /// <summary>Chart slug: tokens by type.</summary>
        public const string ChartTokens = "tokens";

        /// <summary>Chart slug: cost over time.</summary>
        public const string ChartCost = "cost";

        /// <summary>Chart slug: total latency box plot.</summary>
        public const string ChartLatency = "latency";

        /// <summary>Chart slug: time-to-first-token box plot.</summary>
        public const string ChartTtft = "ttft";

        /// <summary>Chart slug: streaming time box plot.</summary>
        public const string ChartStreaming = "streaming";

        /// <summary>Chart slug: throughput box plot.</summary>
        public const string ChartThroughput = "throughput";

        /// <summary>Chart slug: the token heatmap.</summary>
        public const string ChartHeatmap = "heatmap";

        /// <summary>Split metric: total tokens (the default).</summary>
        public const string MetricTokens = "tokens";

        /// <summary>Split metric: calls.</summary>
        public const string MetricCalls = "calls";

        /// <summary>Split metric: cost in USD.</summary>
        public const string MetricCost = "cost";

        /// <summary>The split metrics in picker order: tokens, calls, cost.</summary>
        public static IReadOnlyList<string> Metrics { get; } = new[] { MetricTokens, MetricCalls, MetricCost };

        /// <summary>The box-plot columns after the bucket bounds.</summary>
        public static IReadOnlyList<string> DistributionColumns { get; } = new[] { "min", "avg", "p95", "p99", "max", "count" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalizes a split metric key: "tokens", "calls", or "cost" (case and surrounding whitespace ignored);
        /// anything else is "tokens".
        /// </summary>
        /// <param name="metric">The metric key, or null.</param>
        /// <returns>The normalized key.</returns>
        public static string NormalizeMetric(string? metric)
        {
            string key = (metric ?? string.Empty).Trim().ToLowerInvariant();
            return key == MetricCalls || key == MetricCost ? key : MetricTokens;
        }

        /// <summary>
        /// The value a split metric reads from a bucket's metrics: total tokens, calls, or cost in USD.
        /// </summary>
        /// <param name="metrics">The metrics, or null (0).</param>
        /// <param name="metric">The metric key (normalized with <see cref="NormalizeMetric"/>).</param>
        /// <returns>The value.</returns>
        public static double MetricValue(UsageMetrics? metrics, string? metric)
        {
            if (metrics == null) return 0;
            switch (NormalizeMetric(metric))
            {
                case MetricCalls: return metrics.Calls;
                case MetricCost: return metrics.CostUsd;
                default: return metrics.TotalTokens;
            }
        }

        /// <summary>The "&lt;metric&gt;-by-&lt;dimension&gt;" chart slug, for example "calls-by-callkind".</summary>
        /// <param name="metric">The split metric.</param>
        /// <param name="dimension">The dimension key.</param>
        /// <returns>The slug.</returns>
        public static string SplitChart(string? metric, string? dimension)
        {
            return NormalizeMetric(metric) + "-by-" + Slug(dimension, "model");
        }

        /// <summary>The "usage-by-&lt;dimension&gt;" chart slug, for example "usage-by-endpoint".</summary>
        /// <param name="dimension">The dimension key.</param>
        /// <returns>The slug.</returns>
        public static string UsageByChart(string? dimension)
        {
            return "usage-by-" + Slug(dimension, "model");
        }

        /// <summary>
        /// The export file name: <c>mux-usage-&lt;chart&gt;-&lt;range&gt;-&lt;yyyyMMdd-HHmmss&gt;.csv</c>, the time in local
        /// time. The chart and range are lower-cased with anything outside a-z, 0-9, and '-' replaced by '-'.
        /// </summary>
        /// <param name="chart">The chart slug, for example <see cref="ChartCalls"/> or <see cref="SplitChart"/>.</param>
        /// <param name="range">The range key ("hour" through "all", see <see cref="UsageWindow.KeyOf"/>).</param>
        /// <param name="localNow">The local time of the export.</param>
        /// <param name="extension">The extension without the dot ("csv", or "png" for an image).</param>
        /// <returns>The file name.</returns>
        public static string FileName(string? chart, string? range, DateTime localNow, string extension = "csv")
        {
            return "mux-usage-" + Slug(chart, "chart") + "-" + Slug(range, "range") + "-"
                + localNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "." + Slug(extension, "csv");
        }

        /// <summary>
        /// Formats a number: an empty string for NaN or infinity; otherwise the value rounded to six decimals the way
        /// JavaScript's <c>Math.round(Math.abs(v) * 1e6) / 1e6</c> does (halves away from zero), then an integer as
        /// plain digits and anything else with six decimals and trailing zeros removed. Negative zero is "0".
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The invariant text.</returns>
        public static string FormatNumber(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return string.Empty;
            double rounded = Math.Round(Math.Abs(value) * 1e6, MidpointRounding.AwayFromZero) / 1e6;
            if (rounded == 0) return "0";
            string sign = value < 0 ? "-" : string.Empty;
            if (rounded == Math.Floor(rounded)) return sign + rounded.ToString("0", CultureInfo.InvariantCulture);
            return sign + rounded.ToString("0.000000", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }

        /// <summary>Formats an integer as plain invariant digits.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The text.</returns>
        public static string FormatNumber(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Formats an instant as ISO 8601 local time with the offset in effect then, to the second:
        /// <c>yyyy-MM-ddTHH:mm:ss+HH:mm</c> (UTC is <c>+00:00</c>).
        /// </summary>
        /// <param name="unixMs">The instant, Unix epoch milliseconds.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The text.</returns>
        public static string FormatTime(long unixMs, TimeZoneInfo? zone = null)
        {
            DateTimeOffset utc = DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            DateTimeOffset local = TimeZoneInfo.ConvertTime(utc, zone ?? TimeZoneInfo.Local);
            return local.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Escapes one cell. A cell that a spreadsheet would treat as a formula (it starts with <c>=</c>, <c>+</c>,
        /// <c>@</c>, a tab, CR, or a <c>-</c> that does not begin a number) gets a leading <c>'</c>, so a model or endpoint
        /// name cannot run as a formula when the file is opened. Then it is wrapped in double quotes (doubling any
        /// inside) when it contains a comma, a double quote, CR, or LF, or starts or ends with a space or tab;
        /// otherwise it is returned unchanged. Null is empty.
        /// </summary>
        /// <param name="cell">The cell text.</param>
        /// <returns>The escaped cell.</returns>
        public static string Escape(string? cell)
        {
            if (string.IsNullOrEmpty(cell)) return string.Empty;
            if (IsFormulaLike(cell)) cell = "'" + cell; // a spreadsheet would run it as a formula
            bool quote = cell.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                || cell[0] == ' ' || cell[0] == '\t' || cell[cell.Length - 1] == ' ' || cell[cell.Length - 1] == '\t';
            return quote ? "\"" + cell.Replace("\"", "\"\"") + "\"" : cell;
        }

        /// <summary>True when a cell would be read as a formula by a spreadsheet (see <see cref="Escape"/>).</summary>
        /// <param name="cell">The cell text, not empty.</param>
        /// <returns>Whether to prefix it with an apostrophe.</returns>
        public static bool IsFormulaLike(string cell)
        {
            if (string.IsNullOrEmpty(cell)) return false;
            char first = cell[0];
            if (first == '=' || first == '+' || first == '@' || first == '\t' || first == '\r') return true;
            return first == '-' && (cell.Length == 1 || !(char.IsDigit(cell[1]) || cell[1] == '.'));
        }

        /// <summary>
        /// Writes a header and rows: cells escaped with <see cref="Escape"/>, joined by commas, each line ending in
        /// <see cref="Newline"/>.
        /// </summary>
        /// <param name="header">The column names. Required.</param>
        /// <param name="rows">The rows of already formatted cells, or null for none.</param>
        /// <returns>The CSV text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="header"/> is null.</exception>
        public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>>? rows)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            StringBuilder sb = new StringBuilder();
            AppendLine(sb, header);
            if (rows != null)
            {
                foreach (IReadOnlyList<string> row in rows)
                {
                    if (row != null) AppendLine(sb, row);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// A generic over-time export: <c>bucket_start,bucket_end</c> then one column per name, one row per bucket.
        /// </summary>
        /// <param name="bucketStarts">The bucket starts, Unix ms, oldest first. Required.</param>
        /// <param name="bucketMs">The bucket width in milliseconds.</param>
        /// <param name="columns">The series column names. Required.</param>
        /// <param name="value">The cell for (bucket index, column index): the text from <see cref="FormatNumber(double)"/>
        /// or empty. Required.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string TimeSeries(IReadOnlyList<long> bucketStarts, long bucketMs, IReadOnlyList<string> columns, Func<int, int, string> value, TimeZoneInfo? zone = null)
        {
            if (bucketStarts == null) throw new ArgumentNullException(nameof(bucketStarts));
            if (columns == null) throw new ArgumentNullException(nameof(columns));
            if (value == null) throw new ArgumentNullException(nameof(value));

            List<string> header = new List<string> { BucketStart, BucketEnd };
            header.AddRange(columns);
            List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
            for (int b = 0; b < bucketStarts.Count; b++)
            {
                List<string> row = new List<string> { FormatTime(bucketStarts[b], zone), FormatTime(bucketStarts[b] + bucketMs, zone) };
                for (int c = 0; c < columns.Count; c++) row.Add(value(b, c) ?? string.Empty);
                rows.Add(row);
            }

            return Write(header, rows);
        }

        /// <summary>Calls over time: <c>succeeded</c> (calls minus errors, at least 0) and <c>failed</c>.</summary>
        /// <param name="buckets">The buckets, oldest first, or null.</param>
        /// <param name="bucketMs">The bucket width in milliseconds.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        public static string Calls(IReadOnlyList<UsageBucket>? buckets, long bucketMs, TimeZoneInfo? zone = null)
        {
            List<UsageBucket> list = Clean(buckets);
            return TimeSeries(Starts(list), bucketMs, new[] { "succeeded", "failed" }, (b, c) =>
            {
                UsageMetrics m = list[b].Metrics;
                return FormatNumber(c == 0 ? Math.Max(0, m.Calls - m.Errors) : m.Errors);
            }, zone);
        }

        /// <summary>Tokens by type: <c>uncached_input</c> (input minus cached, at least 0), <c>cached_input</c>, <c>output</c>.</summary>
        /// <param name="buckets">The buckets, oldest first, or null.</param>
        /// <param name="bucketMs">The bucket width in milliseconds.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        public static string Tokens(IReadOnlyList<UsageBucket>? buckets, long bucketMs, TimeZoneInfo? zone = null)
        {
            List<UsageBucket> list = Clean(buckets);
            return TimeSeries(Starts(list), bucketMs, new[] { "uncached_input", "cached_input", "output" }, (b, c) =>
            {
                UsageMetrics m = list[b].Metrics;
                return FormatNumber(c == 0 ? Math.Max(0, m.InputTokens - m.CachedTokens) : c == 1 ? m.CachedTokens : m.OutputTokens);
            }, zone);
        }

        /// <summary>Cost over time: <c>cost_usd</c>.</summary>
        /// <param name="buckets">The buckets, oldest first, or null.</param>
        /// <param name="bucketMs">The bucket width in milliseconds.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        public static string Cost(IReadOnlyList<UsageBucket>? buckets, long bucketMs, TimeZoneInfo? zone = null)
        {
            List<UsageBucket> list = Clean(buckets);
            return TimeSeries(Starts(list), bucketMs, new[] { "cost_usd" }, (b, c) => FormatNumber(list[b].Metrics.CostUsd), zone);
        }

        /// <summary>
        /// A box plot: <c>min,avg,p95,p99,max,count</c> per bucket; the five stats are empty when the bucket has no
        /// samples (count 0).
        /// </summary>
        /// <param name="buckets">The buckets, oldest first, or null.</param>
        /// <param name="bucketMs">The bucket width in milliseconds.</param>
        /// <param name="pick">Selects the distribution from a bucket's metrics. Required.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pick"/> is null.</exception>
        public static string Distribution(IReadOnlyList<UsageBucket>? buckets, long bucketMs, Func<UsageMetrics, UsageDistribution?> pick, TimeZoneInfo? zone = null)
        {
            if (pick == null) throw new ArgumentNullException(nameof(pick));
            List<UsageBucket> list = Clean(buckets);
            return TimeSeries(Starts(list), bucketMs, DistributionColumns, (b, c) =>
            {
                UsageDistribution? d = pick(list[b].Metrics);
                long count = d?.Count ?? 0;
                if (c == 5) return FormatNumber(Math.Max(0, count));
                if (d == null || count <= 0) return string.Empty;
                return FormatNumber(c == 0 ? d.Min : c == 1 ? d.Avg : c == 2 ? d.P95 : c == 3 ? d.P99 : d.Max);
            }, zone);
        }

        /// <summary>
        /// "&lt;Metric&gt; by &lt;dimension&gt;": one column per series (its value, <see cref="UnknownHeader"/> when empty,
        /// <see cref="OtherHeader"/> for the folded remainder, which is always last) holding the metric per bucket.
        /// Series buckets align with <paramref name="bucketStarts"/> by start time; a missing bucket is 0.
        /// </summary>
        /// <param name="bucketStarts">The chart's bucket starts, Unix ms, oldest first. Required.</param>
        /// <param name="bucketMs">The bucket width in milliseconds.</param>
        /// <param name="series">The split series in display order, or null.</param>
        /// <param name="metric">The split metric ("tokens", "calls", or "cost").</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="bucketStarts"/> is null.</exception>
        public static string Split(IReadOnlyList<long> bucketStarts, long bucketMs, IReadOnlyList<UsageSeries>? series, string? metric, TimeZoneInfo? zone = null)
        {
            if (bucketStarts == null) throw new ArgumentNullException(nameof(bucketStarts));
            List<UsageSeries> ordered = new List<UsageSeries>();
            if (series != null)
            {
                foreach (UsageSeries s in series) if (s != null && !s.IsOther) ordered.Add(s);
                foreach (UsageSeries s in series) if (s != null && s.IsOther) ordered.Add(s);
            }

            Dictionary<long, int> index = new Dictionary<long, int>();
            for (int i = 0; i < bucketStarts.Count; i++) index[bucketStarts[i]] = i;
            double[][] values = new double[ordered.Count][];
            List<string> columns = new List<string>();
            for (int s = 0; s < ordered.Count; s++)
            {
                columns.Add(SeriesHeader(ordered[s]));
                values[s] = new double[bucketStarts.Count];
                foreach (UsageBucket bucket in ordered[s].Buckets)
                {
                    if (bucket != null && index.TryGetValue(bucket.BucketStartUnixMs, out int at)) values[s][at] += MetricValue(bucket.Metrics, metric);
                }
            }

            return TimeSeries(bucketStarts, bucketMs, columns, (b, c) => FormatNumber(values[c][b]), zone);
        }

        /// <summary>
        /// "Usage by &lt;dimension&gt;": one row per value in the given order, each with the whole window as its bucket,
        /// then the value and <c>calls,failed,uncached_input,cached_input,output,total_tokens,cost_usd</c>. The value
        /// column is named by the dimension key ("model", "endpoint", "provider", or "callkind").
        /// </summary>
        /// <param name="fromUnixMs">The window start, Unix ms (inclusive).</param>
        /// <param name="toUnixMs">The window end, Unix ms (exclusive).</param>
        /// <param name="dimension">The dimension key.</param>
        /// <param name="rows">The rows in display order, or null.</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        public static string UsageBy(long fromUnixMs, long toUnixMs, string? dimension, IReadOnlyList<UsageBreakdownRow>? rows, TimeZoneInfo? zone = null)
        {
            List<string> header = new List<string>
            {
                BucketStart, BucketEnd, Slug(dimension, "model"), "calls", "failed", "uncached_input", "cached_input", "output", "total_tokens", "cost_usd"
            };
            List<IReadOnlyList<string>> lines = new List<IReadOnlyList<string>>();
            string from = FormatTime(fromUnixMs, zone), to = FormatTime(toUnixMs, zone);
            if (rows != null)
            {
                foreach (UsageBreakdownRow row in rows)
                {
                    if (row == null) continue;
                    UsageMetrics m = row.Metrics ?? new UsageMetrics();
                    lines.Add(new List<string>
                    {
                        from, to, string.IsNullOrWhiteSpace(row.Value) ? "unknown" : row.Value,
                        FormatNumber(m.Calls), FormatNumber(m.Errors), FormatNumber(Math.Max(0, m.InputTokens - m.CachedTokens)),
                        FormatNumber(m.CachedTokens), FormatNumber(m.OutputTokens), FormatNumber(m.TotalTokens), FormatNumber(m.CostUsd)
                    });
                }
            }

            return Write(header, lines);
        }

        /// <summary>
        /// The heatmap: <c>cell_start,cell_end,tokens,calls</c>, one row per cell in chronological order (oldest first,
        /// whatever the grid's display order), skipping cells outside the window (<see cref="UsageHeatmapLayout.IsOutsideWindow"/>:
        /// in the future, or before the month's last 30 days or the year's last 364 days).
        /// </summary>
        /// <param name="heatmap">The heatmap, or null (header only).</param>
        /// <param name="zone">The time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text.</returns>
        public static string Heatmap(UsageHeatmap? heatmap, TimeZoneInfo? zone = null)
        {
            List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
            if (heatmap != null)
            {
                UsageHeatmapLayout layout = heatmap.Layout;
                foreach (int i in layout.CellsInTimeOrder())
                {
                    if (layout.IsOutsideWindow(i)) continue;
                    long start = layout.CellStartUnixMs(i);
                    rows.Add(new[]
                    {
                        FormatTime(start, zone), FormatTime(start + layout.BucketMs, zone),
                        FormatNumber(i < heatmap.TotalTokens.Length ? heatmap.TotalTokens[i] : 0),
                        FormatNumber(i < heatmap.Calls.Length ? heatmap.Calls[i] : 0)
                    });
                }
            }

            return Write(new[] { CellStart, CellEnd, "tokens", "calls" }, rows);
        }

        /// <summary>The column header of a split series: <see cref="OtherHeader"/>, <see cref="UnknownHeader"/> for an empty value, or the value.</summary>
        /// <param name="series">The series. Required.</param>
        /// <returns>The header.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="series"/> is null.</exception>
        public static string SeriesHeader(UsageSeries series)
        {
            if (series == null) throw new ArgumentNullException(nameof(series));
            if (series.IsOther) return OtherHeader;
            return string.IsNullOrWhiteSpace(series.Key) ? UnknownHeader : series.Key;
        }

        #endregion

        #region Private-Methods

        private static void AppendLine(StringBuilder sb, IReadOnlyList<string> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Escape(cells[i]));
            }

            sb.Append(Newline);
        }

        private static List<UsageBucket> Clean(IReadOnlyList<UsageBucket>? buckets)
        {
            List<UsageBucket> list = new List<UsageBucket>();
            if (buckets == null) return list;
            foreach (UsageBucket bucket in buckets)
            {
                if (bucket != null) list.Add(bucket);
            }

            return list;
        }

        private static List<long> Starts(List<UsageBucket> buckets)
        {
            List<long> starts = new List<long>(buckets.Count);
            foreach (UsageBucket bucket in buckets) starts.Add(bucket.BucketStartUnixMs);
            return starts;
        }

        private static string Slug(string? text, string fallback)
        {
            string lower = (text ?? string.Empty).Trim().ToLowerInvariant();
            StringBuilder sb = new StringBuilder(lower.Length);
            foreach (char c in lower) sb.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' ? c : '-');
            return sb.Length > 0 ? sb.ToString() : fallback;
        }

        #endregion
    }
}
