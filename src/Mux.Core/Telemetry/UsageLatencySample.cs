namespace Mux.Core.Telemetry
{
    /// <summary>
    /// A single latency observation used to compute percentiles: the bucket it belongs to plus the
    /// time-to-first-token and total-runtime values for one call. Either timing may be null when the
    /// provider did not report it; null values are excluded from the corresponding percentile.
    /// </summary>
    public sealed class UsageLatencySample
    {
        #region Public-Members

        /// <summary>The bucket start (Unix epoch ms), or 0 when the query is not time-bucketed.</summary>
        public long BucketStartUnixMs { get; set; }

        /// <summary>The time-to-first-token in milliseconds, or null.</summary>
        public long? TimeToFirstTokenMs { get; set; }

        /// <summary>The total request duration in milliseconds, or null.</summary>
        public long? TotalMs { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageLatencySample"/> class.
        /// </summary>
        public UsageLatencySample()
        {
        }

        #endregion
    }
}
