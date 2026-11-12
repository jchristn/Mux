namespace Mux.Desktop.Services
{
    using System.Collections.Generic;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Everything the desktop usage window draws for one range, scope, and dimension: the resolved window,
    /// the KPI summary, the dense time series (with percentiles), the time series split by the selected
    /// dimension, the failure reasons, the whole-range breakdown by the same dimension, and the token heatmap. All of it comes
    /// from the shared <see cref="UsageQueryService"/>, so the numbers match the web dashboard and the TUI.
    /// </summary>
    public sealed class UsageDashboardData
    {
        #region Private-Members

        private UsageSummary _Summary = new UsageSummary();
        private List<UsageBucket> _Timeseries = new List<UsageBucket>();
        private List<UsageSeries> _Split = new List<UsageSeries>();
        private List<UsageErrorCount> _Errors = new List<UsageErrorCount>();
        private List<UsageBreakdownRow> _Breakdown = new List<UsageBreakdownRow>();
        private string _Dimension = "model";
        private UsageHeatmap? _Heatmap;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the bundle for a resolved window.
        /// </summary>
        /// <param name="range">The selected range.</param>
        /// <param name="window">The resolved window. Required.</param>
        public UsageDashboardData(UsageRange range, UsageWindow window)
        {
            Range = range;
            Window = window ?? throw new System.ArgumentNullException(nameof(window));
        }

        #endregion

        #region Public-Members

        /// <summary>The selected range.</summary>
        public UsageRange Range { get; }

        /// <summary>The resolved window (exclusive upper bound) and bucket grid.</summary>
        public UsageWindow Window { get; }

        /// <summary>The split/breakdown dimension the bundle was loaded for.</summary>
        public string Dimension
        {
            get => _Dimension;
            set => _Dimension = string.IsNullOrWhiteSpace(value) ? "model" : value;
        }

        /// <summary>KPI totals over the window.</summary>
        public UsageSummary Summary
        {
            get => _Summary;
            set => _Summary = value ?? new UsageSummary();
        }

        /// <summary>Dense buckets over the window, ascending, with distributions.</summary>
        public List<UsageBucket> Timeseries
        {
            get => _Timeseries;
            set => _Timeseries = value ?? new List<UsageBucket>();
        }

        /// <summary>The time series split by <see cref="Dimension"/>: top values plus "Other" last.</summary>
        public List<UsageSeries> Split
        {
            get => _Split;
            set => _Split = value ?? new List<UsageSeries>();
        }

        /// <summary>Failed calls by error code, most frequent first.</summary>
        public List<UsageErrorCount> Errors
        {
            get => _Errors;
            set => _Errors = value ?? new List<UsageErrorCount>();
        }

        /// <summary>Whole-window totals grouped by <see cref="Dimension"/>, by cost then tokens.</summary>
        public List<UsageBreakdownRow> Breakdown
        {
            get => _Breakdown;
            set => _Breakdown = value ?? new List<UsageBreakdownRow>();
        }

        /// <summary>
        /// The token heatmap on the shared clock-aligned grid (local-time rows; see <see cref="UsageHeatmapLayout"/>),
        /// or null when it was not loaded.
        /// </summary>
        public UsageHeatmap? Heatmap
        {
            get => _Heatmap;
            set => _Heatmap = value;
        }

        #endregion
    }
}
