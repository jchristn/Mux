namespace Mux.Cli.App
{
    using System.Collections.Generic;

    /// <summary>
    /// A plain, pre-computed snapshot of usage telemetry for one time range, rendered by
    /// <see cref="UsageChartsModal"/>. Holds the KPI header line, per-bucket series (tokens, cost, and the
    /// three latency metrics) with matching bucket labels, and a top-models-by-cost breakdown. Free of any
    /// query or widget dependency so the chart modal stays a pure view and the assembly can be unit-tested.
    /// </summary>
    public sealed class UsageChartData
    {
        /// <summary>KPI summary line shown in the header (tokens/cost/calls for the selected range).</summary>
        public List<string> HeaderLines { get; } = new List<string>();

        /// <summary>Short time labels (range-appropriate, e.g. "14:20", "Mon", "09/17") parallel to every per-bucket series.</summary>
        public List<string> BucketLabels { get; } = new List<string>();

        /// <summary>Total tokens per bucket, oldest first; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> TokensPerBucket { get; } = new List<double>();

        /// <summary>Total cost (USD) per bucket, oldest first; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> CostPerBucket { get; } = new List<double>();

        /// <summary>Average time-to-first-token (ms) per bucket; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> TtftMsPerBucket { get; } = new List<double>();

        /// <summary>Average total latency (ms) per bucket; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> TotalMsPerBucket { get; } = new List<double>();

        /// <summary>Average streaming time (ms) per bucket; parallel to <see cref="BucketLabels"/>.</summary>
        public List<double> StreamMsPerBucket { get; } = new List<double>();

        /// <summary>Model names for the top-models-by-cost breakdown, highest first; parallel to <see cref="ModelCosts"/>.</summary>
        public List<string> ModelLabels { get; } = new List<string>();

        /// <summary>Costs (USD) parallel to <see cref="ModelLabels"/>.</summary>
        public List<double> ModelCosts { get; } = new List<double>();
    }
}
