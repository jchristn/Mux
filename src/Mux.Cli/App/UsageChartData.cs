namespace Mux.Cli.App
{
    using System.Collections.Generic;
    using Mux.Core.Telemetry;

    /// <summary>
    /// A plain, pre-computed snapshot of usage telemetry for one time range, rendered by
    /// <see cref="UsageChartsModal"/>. Holds the KPI header line, the resolved window, the per-bucket metrics (calls,
    /// errors, tokens by type, cost, and the four timing distributions) with matching bucket labels, the failure
    /// reasons, and the clock-aligned heatmap. Free of any query or widget dependency so the chart modal stays
    /// a pure view and the assembly can be unit-tested. Build one with <see cref="UsageChartQueries"/>.
    /// </summary>
    public sealed class UsageChartData
    {
        #region Public-Members

        /// <summary>KPI summary line shown in the header (tokens, cost, calls, errors, and error rate for the selected range).</summary>
        public List<string> HeaderLines { get; } = new List<string>();

        /// <summary>The inclusive lower bound of the charted window, Unix epoch milliseconds (0 when unknown).</summary>
        public long WindowFromUnixMs { get; set; }

        /// <summary>The exclusive upper bound of the charted window, Unix epoch milliseconds (0 when unknown).</summary>
        public long WindowToUnixMs { get; set; }

        /// <summary>
        /// The bucket width in milliseconds. 0 means unknown: the modal then derives it from the first two
        /// <see cref="BucketStartsUnixMs"/> values, or from the window for a single bucket.
        /// </summary>
        public long BucketMs { get; set; }

        /// <summary>UTC bucket starts, oldest first; parallel to <see cref="BucketLabels"/> and <see cref="Buckets"/>.</summary>
        public List<long> BucketStartsUnixMs { get; } = new List<long>();

        /// <summary>Short time labels (range-appropriate, e.g. "14:20", "Mon", "11/09", "2026-11-09") parallel to <see cref="Buckets"/>.</summary>
        public List<string> BucketLabels { get; } = new List<string>();

        /// <summary>Per-bucket metrics, oldest first, including the four timing distributions; empty buckets hold zero metrics.</summary>
        public List<UsageMetrics> Buckets { get; } = new List<UsageMetrics>();

        /// <summary>Failure reasons for the window: error codes with counts, most frequent first.</summary>
        public List<UsageErrorCount> ErrorCounts { get; } = new List<UsageErrorCount>();

        /// <summary>
        /// The token heatmap on the shared clock-aligned grid (local-time rows, see <see cref="UsageHeatmapLayout"/>),
        /// independent of the rolling chart buckets; null when none was loaded.
        /// </summary>
        public UsageHeatmap? Heatmap { get; set; }

        #endregion
    }
}
