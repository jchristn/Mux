namespace Mux.Core.Telemetry
{
    /// <summary>
    /// One row of a usage breakdown: a dimension value (a model, endpoint, provider, command, or project)
    /// paired with the derived metrics for all calls that share it. Backs the dashboard's grouped views.
    /// </summary>
    public sealed class UsageBreakdownRow
    {
        #region Private-Members

        private string _Dimension = string.Empty;
        private string _Value = string.Empty;
        private UsageMetrics _Metrics = new UsageMetrics();

        #endregion

        #region Public-Members

        /// <summary>The dimension name (for example "model", "endpoint", "provider", "command").</summary>
        public string Dimension
        {
            get => _Dimension;
            set => _Dimension = value ?? string.Empty;
        }

        /// <summary>The dimension value for this row (for example a specific model id).</summary>
        public string Value
        {
            get => _Value;
            set => _Value = value ?? string.Empty;
        }

        /// <summary>The derived metrics for the group. Never null.</summary>
        public UsageMetrics Metrics
        {
            get => _Metrics;
            set => _Metrics = value ?? new UsageMetrics();
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageBreakdownRow"/> class.
        /// </summary>
        public UsageBreakdownRow()
        {
        }

        #endregion
    }
}
