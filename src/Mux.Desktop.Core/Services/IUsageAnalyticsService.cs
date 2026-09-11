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

        /// <summary>Delete one recorded call by row id; returns the number of rows deleted.</summary>
        Task<int> DeleteEventAsync(long id, CancellationToken token);

        /// <summary>Distinct endpoint names for filter controls.</summary>
        Task<List<string>> GetEndpointsAsync(CancellationToken token);

        /// <summary>Distinct model names for filter controls.</summary>
        Task<List<string>> GetModelsAsync(CancellationToken token);
    }
}
