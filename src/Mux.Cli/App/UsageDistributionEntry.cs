namespace Mux.Cli.App
{
    /// <summary>
    /// One labeled five-number summary (min / avg / p95 / p99 / max, in milliseconds) for the usage
    /// distribution ("candlestick") chart. Pre-computed by the caller so <see cref="UsageChartsModal"/>
    /// stays a pure view.
    /// </summary>
    public sealed class UsageDistributionEntry
    {
        /// <summary>The metric label (for example "TTFT" or "Total").</summary>
        public string Label { get; }

        /// <summary>The minimum value.</summary>
        public double Min { get; }

        /// <summary>The average value (drawn as the box's lower bound).</summary>
        public double Avg { get; }

        /// <summary>The 95th percentile (drawn as the mid marker).</summary>
        public double P95 { get; }

        /// <summary>The 99th percentile (drawn as the box's upper bound).</summary>
        public double P99 { get; }

        /// <summary>The maximum value.</summary>
        public double Max { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageDistributionEntry"/> class.
        /// </summary>
        /// <param name="label">The metric label.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="avg">The average value.</param>
        /// <param name="p95">The 95th percentile.</param>
        /// <param name="p99">The 99th percentile.</param>
        /// <param name="max">The maximum value.</param>
        public UsageDistributionEntry(string label, double min, double avg, double p95, double p99, double max)
        {
            Label = label ?? string.Empty;
            Min = min;
            Avg = avg;
            P95 = p95;
            P99 = p99;
            Max = max;
        }
    }
}
