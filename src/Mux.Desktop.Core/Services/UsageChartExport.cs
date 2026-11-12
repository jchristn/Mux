namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mux.Core.Telemetry;

    /// <summary>
    /// The desktop usage window's chart export, kept out of the Avalonia window so it can be tested: which charts
    /// can switch between bars and lines, whether a chart has data to export, the CSV text (the shared
    /// <see cref="UsageCsv"/> format, so it matches the terminal, web, and VS Code exports), and the suggested file
    /// names. Charts are named by kind: <see cref="Calls"/>, <see cref="Tokens"/>, <see cref="Split"/>,
    /// <see cref="Cost"/>, <see cref="Latency"/>, <see cref="Ttft"/>, <see cref="Streaming"/>,
    /// <see cref="Throughput"/>, <see cref="UsageBy"/>, and <see cref="Heatmap"/>.
    /// </summary>
    public static class UsageChartExport
    {
        /// <summary>Calls over time.</summary>
        public const string Calls = "calls";

        /// <summary>Tokens by type.</summary>
        public const string Tokens = "tokens";

        /// <summary>The selected metric by the selected dimension.</summary>
        public const string Split = "split";

        /// <summary>Cost over time.</summary>
        public const string Cost = "cost";

        /// <summary>Total latency box plot.</summary>
        public const string Latency = "latency";

        /// <summary>Time-to-first-token box plot.</summary>
        public const string Ttft = "ttft";

        /// <summary>Streaming time box plot.</summary>
        public const string Streaming = "streaming";

        /// <summary>Throughput box plot.</summary>
        public const string Throughput = "throughput";

        /// <summary>Whole-range usage by the selected dimension.</summary>
        public const string UsageBy = "usageby";

        /// <summary>The token heatmap.</summary>
        public const string Heatmap = "heatmap";

        /// <summary>Whether a chart stacks series over time and so offers the bars/lines toggle.</summary>
        /// <param name="kind">The chart kind.</param>
        /// <returns>True for calls, tokens, the split chart, and cost.</returns>
        public static bool IsStacked(string? kind)
        {
            return kind == Calls || kind == Tokens || kind == Split || kind == Cost;
        }

        /// <summary>
        /// The chart's slug in export file names: "calls", "tokens", "&lt;metric&gt;-by-&lt;dimension&gt;", "cost",
        /// "latency", "ttft", "streaming", "throughput", "usage-by-&lt;dimension&gt;", or "heatmap".
        /// </summary>
        /// <param name="kind">The chart kind.</param>
        /// <param name="metric">The split metric.</param>
        /// <param name="dimension">The dimension key.</param>
        /// <returns>The slug.</returns>
        public static string ChartSlug(string? kind, string? metric, string? dimension)
        {
            switch (kind)
            {
                case Split: return UsageCsv.SplitChart(metric, UsageChartShaping.NormalizeDimension(dimension));
                case UsageBy: return UsageCsv.UsageByChart(UsageChartShaping.NormalizeDimension(dimension));
                case Tokens: return UsageCsv.ChartTokens;
                case Cost: return UsageCsv.ChartCost;
                case Latency: return UsageCsv.ChartLatency;
                case Ttft: return UsageCsv.ChartTtft;
                case Streaming: return UsageCsv.ChartStreaming;
                case Throughput: return UsageCsv.ChartThroughput;
                case Heatmap: return UsageCsv.ChartHeatmap;
                default: return UsageCsv.ChartCalls;
            }
        }

        /// <summary>
        /// The suggested file name: <c>mux-usage-&lt;chart&gt;-&lt;range&gt;-&lt;yyyyMMdd-HHmmss&gt;.&lt;extension&gt;</c>.
        /// </summary>
        /// <param name="kind">The chart kind.</param>
        /// <param name="metric">The split metric.</param>
        /// <param name="dimension">The dimension key.</param>
        /// <param name="range">The range.</param>
        /// <param name="localNow">The local time of the export.</param>
        /// <param name="extension">"csv" or "png".</param>
        /// <returns>The file name.</returns>
        public static string FileName(string? kind, string? metric, string? dimension, UsageRange range, DateTime localNow, string extension = "csv")
        {
            return UsageCsv.FileName(ChartSlug(kind, metric, dimension), UsageWindow.KeyOf(range), localNow, extension);
        }

        /// <summary>
        /// Whether a chart has data to export (or to save as an image); the toolbar disables both buttons otherwise.
        /// </summary>
        /// <param name="data">The loaded dashboard data, or null.</param>
        /// <param name="kind">The chart kind.</param>
        /// <returns>True when the chart shows data.</returns>
        public static bool HasData(UsageDashboardData? data, string? kind)
        {
            if (data == null) return false;
            switch (kind)
            {
                case Heatmap: return data.Heatmap != null && data.Heatmap.HasUsage();
                case UsageBy: return UsageChartShaping.HasUsage(data.Timeseries) && UsageChartShaping.RankBreakdown(data.Breakdown, null, 1).Count > 0;
                case Split: return UsageChartShaping.HasUsage(data.Timeseries) && UsageChartShaping.SplitStack(data.Split).Count > 0;
                default: return data.Timeseries.Count > 0 && UsageChartShaping.HasUsage(data.Timeseries);
            }
        }

        /// <summary>
        /// The chart's CSV in the shared <see cref="UsageCsv"/> format, or null when it has no data. The split chart
        /// exports its series in stacking order (top values, then Other) for the chosen metric; "Usage by" exports
        /// every value ranked by that metric.
        /// </summary>
        /// <param name="data">The loaded dashboard data, or null.</param>
        /// <param name="kind">The chart kind.</param>
        /// <param name="metric">The split metric.</param>
        /// <param name="zone">The time zone for timestamps; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <returns>The CSV text, or null.</returns>
        public static string? BuildCsv(UsageDashboardData? data, string? kind, string? metric, TimeZoneInfo? zone = null)
        {
            if (data == null || !HasData(data, kind)) return null;
            List<UsageBucket> buckets = data.Timeseries;
            long width = UsageChartShaping.BucketWidthMs(buckets, data.Window);
            switch (kind)
            {
                case Tokens:
                    return UsageCsv.Tokens(buckets, width, zone);
                case Split:
                    List<UsageSeries> series = UsageChartShaping.SplitStack(data.Split).Select(s => s.Series).ToList();
                    return UsageCsv.Split(buckets.Select(b => b.BucketStartUnixMs).ToList(), width, series, metric, zone);
                case Cost:
                    return UsageCsv.Cost(buckets, width, zone);
                case Latency:
                    return UsageCsv.Distribution(buckets, width, m => m.TotalMsDist, zone);
                case Ttft:
                    return UsageCsv.Distribution(buckets, width, m => m.TtftMsDist, zone);
                case Streaming:
                    return UsageCsv.Distribution(buckets, width, m => m.StreamMsDist, zone);
                case Throughput:
                    return UsageCsv.Distribution(buckets, width, m => m.ThroughputDist, zone);
                case UsageBy:
                    return UsageCsv.UsageBy(data.Window.FromUnixMs, data.Window.ToUnixMs, data.Dimension, UsageChartShaping.RankBreakdown(data.Breakdown, metric, 0), zone);
                case Heatmap:
                    return UsageCsv.Heatmap(data.Heatmap, zone);
                default:
                    return UsageCsv.Calls(buckets, width, zone);
            }
        }
    }
}
