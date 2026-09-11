namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Default <see cref="IUsageAnalyticsService"/> over a <see cref="UsageQueryService"/>. Resolves ranges
    /// into the dashboard's fixed windows via <see cref="UsageWindow.Compute"/>, builds a
    /// <see cref="UsageFilter"/>, and delegates to the query service. When telemetry is disabled the query
    /// service returns empty results, matching the dashboard's disabled state.
    /// </summary>
    public sealed class UsageAnalyticsService : IUsageAnalyticsService
    {
        private readonly UsageQueryService _Query;
        private readonly bool _Enabled;
        private readonly Func<long> _NowUnixMs;

        /// <summary>
        /// Instantiate the usage analytics service.
        /// </summary>
        /// <param name="query">The underlying telemetry query service. Required.</param>
        /// <param name="enabled">Whether telemetry capture is enabled (drives the UI empty state).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public UsageAnalyticsService(UsageQueryService query, bool enabled)
            : this(query, enabled, null)
        {
        }

        /// <summary>
        /// Instantiate the usage analytics service with an injectable clock (for tests).
        /// </summary>
        /// <param name="query">The underlying telemetry query service. Required.</param>
        /// <param name="enabled">Whether telemetry capture is enabled.</param>
        /// <param name="nowUnixMs">Clock returning the current Unix epoch milliseconds; null uses the system clock.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public UsageAnalyticsService(UsageQueryService query, bool enabled, Func<long>? nowUnixMs)
        {
            ArgumentNullException.ThrowIfNull(query);
            _Query = query;
            _Enabled = enabled;
            _NowUnixMs = nowUnixMs ?? DefaultNow;
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _Enabled;
        }

        /// <inheritdoc />
        public Task<UsageSummary> GetSummaryAsync(UsageRange range, string? endpoint, string? model, CancellationToken token)
        {
            UsageWindow window = UsageWindow.Compute(range, _NowUnixMs());
            UsageFilter filter = BuildFilter(window, endpoint, model, null);
            return _Query.GetSummaryAsync(filter, token);
        }

        /// <inheritdoc />
        public Task<List<UsageBucket>> GetTimeseriesAsync(UsageRange range, string? endpoint, string? model, CancellationToken token)
        {
            UsageWindow window = UsageWindow.Compute(range, _NowUnixMs());
            UsageFilter filter = BuildFilter(window, endpoint, model, null);
            return _Query.GetTimeseriesAsync(filter, window.BucketMs, token);
        }

        /// <inheritdoc />
        public Task<List<UsageBreakdownRow>> GetBreakdownAsync(string dimension, UsageRange range, string? endpoint, string? model, CancellationToken token)
        {
            UsageWindow window = UsageWindow.Compute(range, _NowUnixMs());
            UsageFilter filter = BuildFilter(window, endpoint, model, null);
            return _Query.GetBreakdownAsync(dimension, filter, token);
        }

        /// <inheritdoc />
        public Task<UsageEventPage> GetEventsAsync(UsageRange range, string? endpoint, string? model, bool? success, int pageNumber, int pageSize, CancellationToken token)
        {
            UsageWindow window = UsageWindow.Compute(range, _NowUnixMs());
            UsageFilter filter = BuildFilter(window, endpoint, model, success);
            return _Query.GetEventsAsync(filter, pageNumber, pageSize, token);
        }

        /// <inheritdoc />
        public Task<int> DeleteEventAsync(long id, CancellationToken token)
        {
            return _Query.DeleteEventAsync(id, token);
        }

        /// <inheritdoc />
        public Task<List<string>> GetEndpointsAsync(CancellationToken token)
        {
            return _Query.GetEndpointsAsync(token);
        }

        /// <inheritdoc />
        public Task<List<string>> GetModelsAsync(CancellationToken token)
        {
            return _Query.GetModelsAsync(token);
        }

        private static UsageFilter BuildFilter(UsageWindow window, string? endpoint, string? model, bool? success)
        {
            return new UsageFilter
            {
                FromUnixMs = window.FromUnixMs,
                ToUnixMs = window.ToUnixMs,
                EndpointName = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint,
                Model = string.IsNullOrWhiteSpace(model) ? null : model,
                Success = success
            };
        }

        private static long DefaultNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
