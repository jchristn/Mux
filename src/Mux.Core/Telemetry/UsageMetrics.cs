namespace Mux.Core.Telemetry
{
    /// <summary>
    /// Derived, presentation-ready usage metrics for a set of calls: token totals, cost, reliability, cache
    /// efficiency, throughput, and latency/TTFT averages and percentiles. Produced by the query service by
    /// rolling up <see cref="UsageAggregateRow"/> values and applying the pricing table. Percentiles are 0
    /// when no timing samples were available.
    /// </summary>
    public sealed class UsageMetrics
    {
        #region Public-Members

        /// <summary>The number of calls.</summary>
        public long Calls { get; set; }

        /// <summary>The number of failed calls.</summary>
        public long Errors { get; set; }

        /// <summary>The failure ratio in the range 0-1 (0 when there were no calls).</summary>
        public double ErrorRate { get; set; }

        /// <summary>Total input/prompt tokens.</summary>
        public long InputTokens { get; set; }

        /// <summary>Total cached (cache-read) input tokens.</summary>
        public long CachedTokens { get; set; }

        /// <summary>Total output/completion tokens.</summary>
        public long OutputTokens { get; set; }

        /// <summary>Total tokens.</summary>
        public long TotalTokens { get; set; }

        /// <summary>Derived cost in US dollars.</summary>
        public double CostUsd { get; set; }

        /// <summary>The cache hit ratio (cached / (input + cached)) in the range 0-1 (0 when no input).</summary>
        public double CacheHitRate { get; set; }

        /// <summary>Mean time-to-first-token in milliseconds (0 when no samples).</summary>
        public double AvgTtftMs { get; set; }

        /// <summary>50th-percentile time-to-first-token in milliseconds.</summary>
        public double P50TtftMs { get; set; }

        /// <summary>95th-percentile time-to-first-token in milliseconds.</summary>
        public double P95TtftMs { get; set; }

        /// <summary>99th-percentile time-to-first-token in milliseconds.</summary>
        public double P99TtftMs { get; set; }

        /// <summary>Mean total request duration in milliseconds (0 when no samples).</summary>
        public double AvgTotalMs { get; set; }

        /// <summary>50th-percentile total request duration in milliseconds.</summary>
        public double P50TotalMs { get; set; }

        /// <summary>95th-percentile total request duration in milliseconds.</summary>
        public double P95TotalMs { get; set; }

        /// <summary>99th-percentile total request duration in milliseconds.</summary>
        public double P99TotalMs { get; set; }

        /// <summary>Mean streaming duration (first token to completion) in milliseconds.</summary>
        public double AvgStreamMs { get; set; }

        /// <summary>Mean output tokens per second.</summary>
        public double AvgTokensPerSec { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageMetrics"/> class.
        /// </summary>
        public UsageMetrics()
        {
        }

        #endregion
    }
}
