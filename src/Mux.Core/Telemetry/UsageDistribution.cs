namespace Mux.Core.Telemetry
{
    using System.Collections.Generic;

    /// <summary>
    /// A five-number summary of a set of per-call samples (minimum, mean, 95th percentile, 99th percentile,
    /// and maximum) plus the sample count. Used to render distribution ("candlestick") charts for latency,
    /// time-to-first-token, streaming duration, and throughput, where each time bucket shows the spread of
    /// its calls rather than a single stacked bar. All values are 0 when there were no samples.
    /// </summary>
    public sealed class UsageDistribution
    {
        #region Public-Members

        /// <summary>The smallest sample value.</summary>
        public double Min { get; set; }

        /// <summary>The mean of the samples.</summary>
        public double Avg { get; set; }

        /// <summary>The 95th-percentile sample value.</summary>
        public double P95 { get; set; }

        /// <summary>The 99th-percentile sample value.</summary>
        public double P99 { get; set; }

        /// <summary>The largest sample value.</summary>
        public double Max { get; set; }

        /// <summary>The number of samples that contributed.</summary>
        public long Count { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageDistribution"/> class.
        /// </summary>
        public UsageDistribution()
        {
        }

        /// <summary>
        /// Builds a distribution from a list of samples. The list is sorted in place. Returns an all-zero
        /// distribution when the list is empty.
        /// </summary>
        /// <param name="values">The sample values (sorted in place).</param>
        /// <param name="percentile">A function that returns the given percentile of the sorted list.</param>
        /// <returns>The distribution.</returns>
        public static UsageDistribution From(List<double> values, System.Func<List<double>, double, double> percentile)
        {
            UsageDistribution dist = new UsageDistribution();
            if (values == null || values.Count == 0)
            {
                return dist;
            }

            values.Sort();
            double sum = 0.0;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            dist.Count = values.Count;
            dist.Min = values[0];
            dist.Max = values[values.Count - 1];
            dist.Avg = sum / values.Count;
            dist.P95 = percentile(values, 0.95);
            dist.P99 = percentile(values, 0.99);
            return dist;
        }

        #endregion
    }
}
