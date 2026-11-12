namespace Mux.Cli.App
{
    using System.Collections.Generic;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Usage split by one dimension (model, endpoint, provider, or call kind) for the window of a
    /// <see cref="UsageChartData"/>: the stacked per-bucket series for the "&lt;Metric&gt; by" page and the whole-range rows
    /// for the "Usage by" page. Rendered by <see cref="UsageChartsModal"/>; built by <see cref="UsageChartQueries"/>.
    /// </summary>
    public sealed class UsageDimensionData
    {
        #region Public-Members

        /// <summary>The dimension key ("model", "endpoint", "provider", or "callkind").</summary>
        public string Dimension { get; set; } = "model";

        /// <summary>
        /// One series per value, ranked by total tokens (top six), with "Other" (<see cref="UsageSeries.IsOther"/>) last.
        /// Buckets align with <see cref="UsageChartData.Buckets"/>.
        /// </summary>
        public List<UsageSeries> Series { get; } = new List<UsageSeries>();

        /// <summary>Whole-range totals per value, sorted by cost then tokens, at most ten rows.</summary>
        public List<UsageBreakdownRow> Rows { get; } = new List<UsageBreakdownRow>();

        /// <summary>
        /// Every whole-range row, sorted by cost then tokens and not capped, so the "Usage by" page can rank by the
        /// chosen metric (see <see cref="UsageChartQueries.RankRows"/>) and export every value. Empty when only
        /// <see cref="Rows"/> was filled.
        /// </summary>
        public List<UsageBreakdownRow> AllRows { get; } = new List<UsageBreakdownRow>();

        #endregion
    }
}
