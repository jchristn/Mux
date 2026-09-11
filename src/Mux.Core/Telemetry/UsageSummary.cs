namespace Mux.Core.Telemetry
{
    /// <summary>
    /// A window-level usage summary: the requested time window plus the derived metrics across all matching
    /// calls. Backs the dashboard KPI strip and the TUI <c>/usage</c> view.
    /// </summary>
    public sealed class UsageSummary
    {
        #region Private-Members

        private UsageMetrics _Metrics = new UsageMetrics();

        #endregion

        #region Public-Members

        /// <summary>The inclusive lower bound of the window (Unix epoch ms), or 0 when unbounded.</summary>
        public long FromUnixMs { get; set; }

        /// <summary>The upper bound of the window (Unix epoch ms), or 0 when unbounded.</summary>
        public long ToUnixMs { get; set; }

        /// <summary>The derived metrics across the window. Never null.</summary>
        public UsageMetrics Metrics
        {
            get => _Metrics;
            set => _Metrics = value ?? new UsageMetrics();
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageSummary"/> class.
        /// </summary>
        public UsageSummary()
        {
        }

        #endregion
    }
}
