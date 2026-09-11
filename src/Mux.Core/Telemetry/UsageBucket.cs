namespace Mux.Core.Telemetry
{
    /// <summary>
    /// One point in a usage time series: the bucket start plus the derived metrics for all calls that fell
    /// in the bucket. The dashboard selects whichever metric it is charting from <see cref="Metrics"/>.
    /// </summary>
    public sealed class UsageBucket
    {
        #region Private-Members

        private UsageMetrics _Metrics = new UsageMetrics();

        #endregion

        #region Public-Members

        /// <summary>The bucket start as Unix epoch milliseconds (UTC).</summary>
        public long BucketStartUnixMs { get; set; }

        /// <summary>The derived metrics for the bucket. Never null.</summary>
        public UsageMetrics Metrics
        {
            get => _Metrics;
            set => _Metrics = value ?? new UsageMetrics();
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageBucket"/> class.
        /// </summary>
        public UsageBucket()
        {
        }

        #endregion
    }
}
