namespace Mux.Desktop.Services
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;

    /// <summary>
    /// The desktop app's seam over <see cref="UsageQueryService"/>. Resolves a <see cref="UsageRange"/> into
    /// the dashboard's fixed window/bucketing, applies endpoint/model/success filters, and returns the same
    /// telemetry DTOs the web dashboard renders — so native charts, KPIs, and tables match it exactly.
    /// </summary>
    public interface IUsageAnalyticsService
    {
        /// <summary>Whether telemetry is enabled and the store is available.</summary>
        bool IsEnabled { get; }

        /// <summary>KPI summary metrics over the range.</summary>
        Task<UsageSummary> GetSummaryAsync(UsageRange range, string? endpoint, string? model, CancellationToken token);

        /// <summary>Dense, zero-filled time series over the range for charting.</summary>
        Task<List<UsageBucket>> GetTimeseriesAsync(UsageRange range, string? endpoint, string? model, CancellationToken token);

        /// <summary>Totals grouped by a dimension (model/endpoint/provider/command/callkind) over the range.</summary>
        Task<List<UsageBreakdownRow>> GetBreakdownAsync(string dimension, UsageRange range, string? endpoint, string? model, CancellationToken token);

        /// <summary>A page of raw per-call events over the range, newest first.</summary>
        Task<UsageEventPage> GetEventsAsync(UsageRange range, string? endpoint, string? model, bool? success, int pageNumber, int pageSize, CancellationToken token);

        /// <summary>
        /// Everything the usage window charts for a range: the resolved window (the "all" range sized from the
        /// oldest matching call), KPI summary, dense time series with percentiles, the time series split by
        /// <paramref name="dimension"/> (top six plus "Other"), failure reasons, and the breakdown by the same
        /// dimension, all scoped by <paramref name="scope"/>.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="scope">The endpoint/conversation/model scope. Null means no constraint.</param>
        /// <param name="dimension">The split/breakdown dimension: model, endpoint, provider, or callkind.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The chart data.</returns>
        Task<UsageDashboardData> LoadDashboardAsync(UsageRange range, UsageScope? scope, string dimension, CancellationToken token);

        /// <summary>
        /// The newest calls in an explicit window (inclusive bounds), scoped by <paramref name="scope"/>; used for
        /// the call table and for drilling into one chart bucket.
        /// </summary>
        /// <param name="fromUnixMs">The inclusive lower bound, Unix epoch milliseconds.</param>
        /// <param name="toUnixMs">The inclusive upper bound, Unix epoch milliseconds.</param>
        /// <param name="scope">The endpoint/conversation/model scope. Null means no constraint.</param>
        /// <param name="pageSize">The maximum number of rows (clamped 1-500 by the store).</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The newest-first page with the total matching count.</returns>
        Task<UsageEventPage> GetEventsInWindowAsync(long fromUnixMs, long toUnixMs, UsageScope? scope, int pageSize, CancellationToken token);

        /// <summary>Delete one recorded call by row id; returns the number of rows deleted.</summary>
        Task<int> DeleteEventAsync(long id, CancellationToken token);

        /// <summary>Distinct endpoint names for filter controls.</summary>
        Task<List<string>> GetEndpointsAsync(CancellationToken token);

        /// <summary>Distinct model names for filter controls.</summary>
        Task<List<string>> GetModelsAsync(CancellationToken token);
    }
}
