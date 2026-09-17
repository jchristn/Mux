namespace Mux.Cli.App
{
    using System.Collections.Generic;

    /// <summary>
    /// A plain, pre-computed snapshot of usage telemetry for <see cref="UsageChartsModal"/> to render. Holds
    /// the KPI header lines, a per-day tokens and cost series (with matching day labels), and a top-models
    /// breakdown (labels with matching costs). Keeping it free of any query or widget dependency makes the
    /// chart modal a pure view and lets the data assembly be unit-tested on its own.
    /// </summary>
    public sealed class UsageChartData
    {
        /// <summary>KPI summary lines shown above the charts (for example tokens/cost/calls for each window).</summary>
        public List<string> HeaderLines { get; } = new List<string>();

        /// <summary>Total tokens per day over the trailing window, oldest first.</summary>
        public List<double> TokensPerDay { get; } = new List<double>();

        /// <summary>Total cost (USD) per day over the trailing window, oldest first; parallel to <see cref="DayLabels"/>.</summary>
        public List<double> CostPerDay { get; } = new List<double>();

        /// <summary>Short day labels (for example "Mon") parallel to <see cref="TokensPerDay"/> and <see cref="CostPerDay"/>.</summary>
        public List<string> DayLabels { get; } = new List<string>();

        /// <summary>Model names for the top-models-by-cost breakdown, highest first; parallel to <see cref="ModelCosts"/>.</summary>
        public List<string> ModelLabels { get; } = new List<string>();

        /// <summary>Costs (USD) parallel to <see cref="ModelLabels"/>.</summary>
        public List<double> ModelCosts { get; } = new List<double>();

        /// <summary>Latency distributions (min/avg/p95/p99/max, ms) rendered as a box-and-whisker chart.</summary>
        public List<UsageDistributionEntry> Distributions { get; } = new List<UsageDistributionEntry>();
    }
}
