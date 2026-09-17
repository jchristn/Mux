namespace Mux.Cli.App
{
    using System.Collections.Generic;

    /// <summary>
    /// A plain, pre-computed snapshot of usage telemetry for one time range, rendered by
    /// <see cref="UsageChartsModal"/>. Holds the KPI header lines, a per-bucket tokens and cost series (with
    /// matching bucket labels), a top-models breakdown (labels with matching costs), and latency
    /// distributions. Free of any query or widget dependency so the chart modal stays a pure view and the
    /// assembly can be unit-tested on its own.
    /// </summary>
    public sealed class UsageChartData
    {
        /// <summary>KPI summary lines shown in the header (tokens/cost/calls for the selected range).</summary>
        public List<string> HeaderLines { get; } = new List<string>();

        /// <summary>Total tokens per bucket over the range, oldest first; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> TokensPerBucket { get; } = new List<double>();

        /// <summary>Total cost (USD) per bucket over the range, oldest first; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> CostPerBucket { get; } = new List<double>();

        /// <summary>Short time labels (range-appropriate, e.g. "14:20", "Mon", "09/17") parallel to the per-bucket series.</summary>
        public List<string> BucketLabels { get; } = new List<string>();

        /// <summary>Model names for the top-models-by-cost breakdown, highest first; parallel to <see cref="ModelCosts"/>.</summary>
        public List<string> ModelLabels { get; } = new List<string>();

        /// <summary>Costs (USD) parallel to <see cref="ModelLabels"/>.</summary>
        public List<double> ModelCosts { get; } = new List<double>();

        /// <summary>Latency distributions (min/avg/p95/p99/max, ms) rendered as a box-and-whisker chart.</summary>
        public List<UsageDistributionEntry> Distributions { get; } = new List<UsageDistributionEntry>();
    }
}
