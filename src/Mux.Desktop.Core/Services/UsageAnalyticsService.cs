namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Default <see cref="IUsageAnalyticsService"/> over a <see cref="UsageQueryService"/>. Resolves ranges
    /// into the shared windows via <see cref="UsageQueryService.ResolveWindowAsync"/> (so "all" is sized from
    /// the oldest matching call), builds a <see cref="UsageFilter"/>, and delegates to the query service. When telemetry is disabled the query
    /// service returns empty results, matching the dashboard's disabled state.
    /// </summary>
    public sealed class UsageAnalyticsService : IUsageAnalyticsService
    {
        // Per-endpoint split depth when several endpoints are combined, so little is pre-folded into "Other".
        private const int MergeSplitTop = 20;

        private readonly UsageQueryService _Query;
        private readonly bool _Enabled;
        private readonly Func<long> _NowUnixMs;
        private readonly TimeZoneInfo _Zone;

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
            : this(query, enabled, nowUnixMs, null)
        {
        }

        /// <summary>
        /// Instantiate the usage analytics service with an injectable clock and time zone (for tests).
        /// </summary>
        /// <param name="query">The underlying telemetry query service. Required.</param>
        /// <param name="enabled">Whether telemetry capture is enabled.</param>
        /// <param name="nowUnixMs">Clock returning the current Unix epoch milliseconds; null uses the system clock.</param>
        /// <param name="zone">The time zone the heatmap rows align to; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="query"/> is null.</exception>
        public UsageAnalyticsService(UsageQueryService query, bool enabled, Func<long>? nowUnixMs, TimeZoneInfo? zone)
        {
            ArgumentNullException.ThrowIfNull(query);
            _Zone = zone ?? TimeZoneInfo.Local;
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
        public async Task<UsageSummary> GetSummaryAsync(UsageRange range, string? endpoint, string? model, CancellationToken token)
        {
            UsageFilter filter = await WindowedFilterAsync(range, endpoint, model, null, token).ConfigureAwait(false);
            return await _Query.GetSummaryAsync(filter, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<UsageBucket>> GetTimeseriesAsync(UsageRange range, string? endpoint, string? model, CancellationToken token)
        {
            UsageFilter probe = BuildFilter(endpoint, model, null);
            UsageWindow window = await _Query.ResolveWindowAsync(range, probe, _NowUnixMs(), token).ConfigureAwait(false);
            ApplyWindow(probe, window);
            return await _Query.GetTimeseriesAsync(probe, window.BucketMs, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<UsageBreakdownRow>> GetBreakdownAsync(string dimension, UsageRange range, string? endpoint, string? model, CancellationToken token)
        {
            UsageFilter filter = await WindowedFilterAsync(range, endpoint, model, null, token).ConfigureAwait(false);
            return await _Query.GetBreakdownAsync(dimension, filter, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<UsageEventPage> GetEventsAsync(UsageRange range, string? endpoint, string? model, bool? success, int pageNumber, int pageSize, CancellationToken token)
        {
            UsageFilter filter = await WindowedFilterAsync(range, endpoint, model, success, token).ConfigureAwait(false);
            return await _Query.GetEventsAsync(filter, pageNumber, pageSize, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<UsageDashboardData> LoadDashboardAsync(UsageRange range, UsageScope? scope, string dimension, CancellationToken token)
        {
            string dim = UsageChartShaping.NormalizeDimension(dimension);
            List<UsageFilter> filters = ScopeFilters(scope);
            long now = _NowUnixMs();

            UsageWindow window;
            long earliest = 0;
            if (filters.Count == 1)
            {
                if (range == UsageRange.All)
                {
                    earliest = await _Query.GetEarliestEventUnixMsAsync(filters[0], token).ConfigureAwait(false);
                }

                window = UsageWindow.Compute(range, now, earliest);
            }
            else
            {
                if (range == UsageRange.All)
                {
                    foreach (UsageFilter filter in filters)
                    {
                        long oldest = await _Query.GetEarliestEventUnixMsAsync(filter, token).ConfigureAwait(false);
                        if (oldest > 0 && (earliest == 0 || oldest < earliest))
                        {
                            earliest = oldest;
                        }
                    }
                }

                window = UsageWindow.Compute(range, now, earliest);
            }

            foreach (UsageFilter filter in filters)
            {
                ApplyWindow(filter, window);
            }

            // The heatmap uses the shared clock-aligned grid in local time, independent of the chart window.
            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(range, now, _Zone, earliest);
            UsageDashboardData data = new UsageDashboardData(range, window) { Dimension = dim };
            if (filters.Count == 1)
            {
                UsageFilter only = filters[0];
                data.Heatmap = await _Query.GetHeatmapAsync(layout, only, token).ConfigureAwait(false);
                data.Summary = await _Query.GetSummaryAsync(only, token).ConfigureAwait(false);
                data.Timeseries = await _Query.GetTimeseriesAsync(only, window.BucketMs, token).ConfigureAwait(false);
                data.Split = await _Query.GetSplitTimeseriesAsync(dim, only, window.BucketMs, UsageChartShaping.SplitTop, token).ConfigureAwait(false);
                data.Errors = await _Query.GetErrorCountsAsync(only, token).ConfigureAwait(false);
                data.Breakdown = await _Query.GetBreakdownAsync(dim, only, token).ConfigureAwait(false);
                return data;
            }

            // The endpoint filter matched several endpoints: query each one and combine (see UsageMerge).
            List<UsageMetrics> summaries = new List<UsageMetrics>();
            List<List<UsageBucket>> timeseries = new List<List<UsageBucket>>();
            List<List<UsageSeries>> splits = new List<List<UsageSeries>>();
            List<List<UsageErrorCount>> errors = new List<List<UsageErrorCount>>();
            List<List<UsageBreakdownRow>> breakdowns = new List<List<UsageBreakdownRow>>();
            List<UsageHeatmap> heatmaps = new List<UsageHeatmap>();
            foreach (UsageFilter filter in filters)
            {
                heatmaps.Add(await _Query.GetHeatmapAsync(layout, filter, token).ConfigureAwait(false));
                summaries.Add((await _Query.GetSummaryAsync(filter, token).ConfigureAwait(false)).Metrics);
                timeseries.Add(await _Query.GetTimeseriesAsync(filter, window.BucketMs, token).ConfigureAwait(false));
                splits.Add(await _Query.GetSplitTimeseriesAsync(dim, filter, window.BucketMs, MergeSplitTop, token).ConfigureAwait(false));
                errors.Add(await _Query.GetErrorCountsAsync(filter, token).ConfigureAwait(false));
                breakdowns.Add(await _Query.GetBreakdownAsync(dim, filter, token).ConfigureAwait(false));
            }

            data.Summary = new UsageSummary
            {
                FromUnixMs = filters[0].FromUnixMs,
                ToUnixMs = filters[0].ToUnixMs,
                Metrics = UsageMerge.MergeMetrics(summaries)
            };
            data.Timeseries = UsageMerge.MergeBuckets(timeseries);
            data.Split = UsageMerge.MergeSplit(splits, UsageChartShaping.SplitTop);
            data.Errors = UsageMerge.MergeErrors(errors);
            data.Breakdown = UsageMerge.MergeBreakdown(breakdowns);
            data.Heatmap = UsageHeatmap.Merge(layout, heatmaps);
            return data;
        }

        /// <inheritdoc />
        public async Task<UsageEventPage> GetEventsInWindowAsync(long fromUnixMs, long toUnixMs, UsageScope? scope, int pageSize, CancellationToken token)
        {
            List<UsageFilter> filters = ScopeFilters(scope);
            List<UsageEventPage> pages = new List<UsageEventPage>();
            foreach (UsageFilter filter in filters)
            {
                filter.FromUnixMs = fromUnixMs;
                filter.ToUnixMs = toUnixMs;
                pages.Add(await _Query.GetEventsAsync(filter, 1, pageSize, token).ConfigureAwait(false));
            }

            return pages.Count == 1 ? pages[0] : UsageMerge.MergeEventPages(pages, Math.Clamp(pageSize, 1, 500));
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

        private async Task<UsageFilter> WindowedFilterAsync(UsageRange range, string? endpoint, string? model, bool? success, CancellationToken token)
        {
            UsageFilter filter = BuildFilter(endpoint, model, success);
            UsageWindow window = await _Query.ResolveWindowAsync(range, filter, _NowUnixMs(), token).ConfigureAwait(false);
            ApplyWindow(filter, window);
            return filter;
        }

        private static UsageFilter BuildFilter(string? endpoint, string? model, bool? success)
        {
            return new UsageFilter
            {
                EndpointName = string.IsNullOrWhiteSpace(endpoint) ? null : endpoint,
                Model = string.IsNullOrWhiteSpace(model) ? null : model,
                Success = success
            };
        }

        // The window's upper bound is exclusive; the store's filter bound is inclusive.
        private static void ApplyWindow(UsageFilter filter, UsageWindow window)
        {
            filter.FromUnixMs = window.FromUnixMs;
            filter.ToUnixMs = window.ToUnixMs - 1;
        }

        // One filter per matched endpoint (or a single filter with no endpoint constraint). A constraint that
        // matched nothing becomes an empty session set, which the store turns into an empty result.
        private static List<UsageFilter> ScopeFilters(UsageScope? scope)
        {
            UsageScope s = scope ?? UsageScope.Any;
            if (s.MatchesNothing)
            {
                return new List<UsageFilter> { new UsageFilter { SessionIds = Array.Empty<string>() } };
            }

            List<UsageFilter> filters = new List<UsageFilter>();
            if (s.Endpoints == null)
            {
                filters.Add(new UsageFilter { Model = s.Model, SessionIds = s.SessionIds });
                return filters;
            }

            foreach (string endpoint in s.Endpoints)
            {
                filters.Add(new UsageFilter { EndpointName = endpoint, Model = s.Model, SessionIds = s.SessionIds });
            }

            return filters;
        }

        private static long DefaultNow()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
