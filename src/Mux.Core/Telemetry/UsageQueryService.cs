namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;

    /// <summary>
    /// Read-side service over a <see cref="SqliteUsageStore"/>: composes aggregate rows and latency samples
    /// into window summaries, time series, and breakdowns, applying the pricing table for cost and computing
    /// latency/TTFT percentiles in memory. The pricing table is resolved per query so a rate edit takes
    /// effect immediately. All methods are best-effort at the call site (the store surfaces failures as
    /// exceptions for the caller to handle).
    /// </summary>
    public sealed class UsageQueryService
    {
        #region Private-Members

        private readonly SqliteUsageStore _Store;
        private readonly Func<PricingTable> _PricingProvider;
        private readonly ISessionMetadataIndex? _SessionIndex;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageQueryService"/> class.
        /// </summary>
        /// <param name="store">The backing store. Required.</param>
        /// <param name="pricingProvider">A provider of the current pricing table. Required; called per query.</param>
        /// <param name="sessionIndex">An optional session label/tag index. When supplied, usage can be filtered
        /// and broken down by label/tag (resolved to session ids at query time). When null, label/tag filters
        /// match nothing and label/tag breakdowns are empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public UsageQueryService(SqliteUsageStore store, Func<PricingTable> pricingProvider, ISessionMetadataIndex? sessionIndex = null)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _PricingProvider = pricingProvider ?? throw new ArgumentNullException(nameof(pricingProvider));
            _SessionIndex = sessionIndex;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Computes a window-level usage summary.
        /// </summary>
        /// <param name="filter">The filter/window to summarize. Null summarizes everything.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The summary.</returns>
        public async Task<UsageSummary> GetSummaryAsync(UsageFilter? filter, CancellationToken token)
        {
            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            PricingTable pricing = _PricingProvider();
            List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(filter, 0, token).ConfigureAwait(false);
            List<UsageLatencySample> samples = await _Store.FetchLatencySamplesAsync(filter, 0, token).ConfigureAwait(false);

            UsageMetrics metrics = RollUp(rows, pricing);
            ApplyPercentiles(metrics, samples);

            return new UsageSummary
            {
                FromUnixMs = filter?.FromUnixMs ?? 0,
                ToUnixMs = filter?.ToUnixMs ?? 0,
                Metrics = metrics
            };
        }

        /// <summary>
        /// Computes a bucketed time series over the window.
        /// </summary>
        /// <param name="filter">The filter/window. Null covers everything.</param>
        /// <param name="bucketMs">The bucket width in ms (for example 3600000 hourly, 86400000 daily). Floored at 1.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The buckets, ascending by time.</returns>
        public async Task<List<UsageBucket>> GetTimeseriesAsync(UsageFilter? filter, long bucketMs, CancellationToken token)
        {
            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            long width = bucketMs < 1 ? 1 : bucketMs;
            PricingTable pricing = _PricingProvider();
            List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(filter, width, token).ConfigureAwait(false);
            List<UsageLatencySample> samples = await _Store.FetchLatencySamplesAsync(filter, width, token).ConfigureAwait(false);

            Dictionary<long, List<UsageLatencySample>> samplesByBucket = new Dictionary<long, List<UsageLatencySample>>();
            foreach (UsageLatencySample sample in samples)
            {
                if (!samplesByBucket.TryGetValue(sample.BucketStartUnixMs, out List<UsageLatencySample>? list))
                {
                    list = new List<UsageLatencySample>();
                    samplesByBucket[sample.BucketStartUnixMs] = list;
                }

                list.Add(sample);
            }

            Dictionary<long, List<UsageAggregateRow>> rowsByBucket = new Dictionary<long, List<UsageAggregateRow>>();
            foreach (UsageAggregateRow row in rows)
            {
                if (!rowsByBucket.TryGetValue(row.BucketStartUnixMs, out List<UsageAggregateRow>? list))
                {
                    list = new List<UsageAggregateRow>();
                    rowsByBucket[row.BucketStartUnixMs] = list;
                }

                list.Add(row);
            }

            List<UsageBucket> buckets = new List<UsageBucket>();

            // When the window is bounded, emit a DENSE, contiguous series: one bucket for every step from the
            // aligned start through the end, zero-filling buckets that had no calls. This gives the chart a
            // fixed, evenly-spaced set of time slices (e.g. 60 one-minute slices for the last hour) instead of
            // only the buckets that happened to have activity.
            if (filter != null && filter.FromUnixMs > 0 && filter.ToUnixMs > 0)
            {
                long start = (filter.FromUnixMs / width) * width;
                int guard = 0;
                while (start <= filter.ToUnixMs && guard < 100000)
                {
                    guard++;
                    UsageMetrics metrics = rowsByBucket.TryGetValue(start, out List<UsageAggregateRow>? grp)
                        ? RollUp(grp, pricing)
                        : new UsageMetrics();
                    if (samplesByBucket.TryGetValue(start, out List<UsageLatencySample>? bs))
                    {
                        ApplyPercentiles(metrics, bs);
                    }

                    buckets.Add(new UsageBucket { BucketStartUnixMs = start, Metrics = metrics });
                    start += width;
                }

                return buckets;
            }

            // Unbounded window: emit only the buckets that had activity, ascending.
            foreach (IGrouping<long, UsageAggregateRow> group in rows.GroupBy(r => r.BucketStartUnixMs).OrderBy(g => g.Key))
            {
                UsageMetrics metrics = RollUp(group, pricing);
                if (samplesByBucket.TryGetValue(group.Key, out List<UsageLatencySample>? bucketSamples))
                {
                    ApplyPercentiles(metrics, bucketSamples);
                }

                buckets.Add(new UsageBucket { BucketStartUnixMs = group.Key, Metrics = metrics });
            }

            return buckets;
        }

        /// <summary>Returns totals and timing means per model (without percentiles), using the total series filters and bucket boundaries.</summary>
        /// <param name="filter">The usage scope and time window.</param>
        /// <param name="bucketMs">Bucket width in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Model names mapped to ascending buckets, including empty buckets in bounded windows.</returns>
        public async Task<Dictionary<string, List<UsageBucket>>> GetModelTimeseriesAsync(UsageFilter? filter, long bucketMs, CancellationToken token)
        {
            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            long width = Math.Max(1, bucketMs);
            PricingTable pricing = _PricingProvider();
            List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(filter, width, token).ConfigureAwait(false);
            Dictionary<string, List<UsageBucket>> result = new(StringComparer.Ordinal);
            foreach (var model in rows.GroupBy(r => r.Model).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var groups = model.GroupBy(r => r.BucketStartUnixMs).ToDictionary(g => g.Key, g => RollUp(g, pricing));
                List<UsageBucket> buckets = new();
                if (filter != null && filter.FromUnixMs > 0 && filter.ToUnixMs > 0)
                {
                    long start = filter.FromUnixMs / width * width;
                    for (int guard = 0; start <= filter.ToUnixMs && guard < 100000; guard++, start += width)
                        buckets.Add(new UsageBucket { BucketStartUnixMs = start, Metrics = groups.TryGetValue(start, out var metrics) ? metrics : new UsageMetrics() });
                }
                else
                {
                    foreach (var bucket in groups.OrderBy(g => g.Key))
                        buckets.Add(new UsageBucket { BucketStartUnixMs = bucket.Key, Metrics = bucket.Value });
                }
                result[model.Key] = buckets;
            }
            return result;
        }

        /// <summary>
        /// The dimensions a time series can be split by (see <see cref="GetSplitTimeseriesAsync"/>).
        /// </summary>
        public static IReadOnlyList<string> SplitDimensions { get; } = new[] { "model", "endpoint", "provider", "command", "callkind" };

        /// <summary>
        /// Splits the time series by a dimension: one series per value, ranked by total tokens over the window
        /// (ties by cost, then name), keeping the top <paramref name="top"/> and folding the rest into one series
        /// keyed "Other" (<see cref="UsageSeries.IsOther"/>). Every series has the same buckets as
        /// <see cref="GetTimeseriesAsync"/> for the same filter and width (dense in a bounded window), so the
        /// series stack. Buckets carry totals and timing means, not percentiles.
        /// </summary>
        /// <param name="dimension">One of <see cref="SplitDimensions"/> (case-insensitive; "adapter" means provider,
        /// "call_kind" means callkind).</param>
        /// <param name="filter">The filter/window. Null covers everything.</param>
        /// <param name="bucketMs">The bucket width in ms. Floored at 1.</param>
        /// <param name="top">How many values to keep before folding the rest into "Other"; clamped to 1-20.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The series, ranked, with "Other" (when present) last.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="dimension"/> is not a split dimension.</exception>
        public async Task<List<UsageSeries>> GetSplitTimeseriesAsync(string dimension, UsageFilter? filter, long bucketMs, int top, CancellationToken token)
        {
            string requested = (dimension ?? string.Empty).Trim().ToLowerInvariant();
            string dim = NormalizeDimension(requested);
            if (requested.Length > 0 && dim == "model" && requested != "model")
            {
                throw new ArgumentException("Unknown split dimension '" + dimension + "'. Use one of: " + string.Join(", ", SplitDimensions) + ".", nameof(dimension));
            }

            if (dim == "label" || dim == "tag")
            {
                throw new ArgumentException("Usage cannot be split over time by " + dim + "; a session can carry several. Use one of: " + string.Join(", ", SplitDimensions) + ".", nameof(dimension));
            }

            int keep = Math.Clamp(top, 1, 20);
            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            long width = Math.Max(1, bucketMs);
            PricingTable pricing = _PricingProvider();
            List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(filter, width, token).ConfigureAwait(false);

            List<IGrouping<string, UsageAggregateRow>> ranked = rows
                .GroupBy(r => KeyForDimension(dim, r), StringComparer.Ordinal)
                .Select(g => new { Group = g, Metrics = RollUp(g, pricing) })
                .OrderByDescending(x => x.Metrics.TotalTokens)
                .ThenByDescending(x => x.Metrics.CostUsd)
                .ThenBy(x => x.Group.Key, StringComparer.Ordinal)
                .Select(x => x.Group)
                .ToList();

            // Fold everything past the top N into one series, but only when that leaves at least two values to fold:
            // an "Other" holding a single value would just hide its name.
            bool fold = ranked.Count > keep + 1;
            List<(string Key, bool IsOther, List<UsageAggregateRow> Rows)> groups = new List<(string, bool, List<UsageAggregateRow>)>();
            for (int i = 0; i < ranked.Count && (!fold || i < keep); i++)
            {
                groups.Add((ranked[i].Key, false, ranked[i].ToList()));
            }

            if (fold)
            {
                groups.Add(("Other", true, ranked.Skip(keep).SelectMany(g => g).ToList()));
            }

            List<long> starts = new List<long>();
            if (filter != null && filter.FromUnixMs > 0 && filter.ToUnixMs > 0)
            {
                long start = (filter.FromUnixMs / width) * width;
                for (int guard = 0; start <= filter.ToUnixMs && guard < 100000; guard++, start += width)
                {
                    starts.Add(start);
                }
            }
            else
            {
                starts = rows.Select(r => r.BucketStartUnixMs).Distinct().OrderBy(s => s).ToList();
            }

            List<UsageSeries> result = new List<UsageSeries>();
            foreach ((string key, bool isOther, List<UsageAggregateRow> groupRows) in groups)
            {
                Dictionary<long, UsageMetrics> byBucket = groupRows
                    .GroupBy(r => r.BucketStartUnixMs)
                    .ToDictionary(g => g.Key, g => RollUp(g, pricing));
                UsageSeries series = new UsageSeries { Key = key, IsOther = isOther };
                foreach (long start in starts)
                {
                    series.Buckets.Add(new UsageBucket
                    {
                        BucketStartUnixMs = start,
                        Metrics = byBucket.TryGetValue(start, out UsageMetrics? metrics) ? metrics : new UsageMetrics()
                    });
                }

                result.Add(series);
            }

            return result;
        }

        /// <summary>
        /// Counts the failed calls in the window by error code, most frequent first. Failures with no recorded
        /// code are counted under "unknown".
        /// </summary>
        /// <param name="filter">The filter/window. Null covers everything.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The error counts.</returns>
        public async Task<List<UsageErrorCount>> GetErrorCountsAsync(UsageFilter? filter, CancellationToken token)
        {
            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            List<KeyValuePair<string, long>> counts = await _Store.FetchErrorCodeCountsAsync(filter, token).ConfigureAwait(false);
            return counts.Select(c => new UsageErrorCount { ErrorCode = c.Key, Count = c.Value }).ToList();
        }

        /// <summary>
        /// Returns the timestamp of the oldest event matching the filter's non-time constraints (its window is
        /// ignored), or 0 when nothing matches. Sizes the <see cref="UsageRange.All"/> window.
        /// </summary>
        /// <param name="filter">The filter. Null matches everything.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The oldest event's Unix-ms timestamp, or 0.</returns>
        public async Task<long> GetEarliestEventUnixMsAsync(UsageFilter? filter, CancellationToken token)
        {
            UsageFilter? unbounded = null;
            if (filter != null)
            {
                unbounded = new UsageFilter
                {
                    EndpointName = filter.EndpointName,
                    Model = filter.Model,
                    SessionId = filter.SessionId,
                    Labels = filter.Labels,
                    Tags = filter.Tags,
                    SessionIds = filter.SessionIds,
                    CallKind = filter.CallKind,
                    Success = filter.Success
                };
            }

            unbounded = await ResolveFilterAsync(unbounded, token).ConfigureAwait(false);
            return await _Store.FetchEarliestTimestampAsync(unbounded, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a range to its window at <paramref name="nowUnixMs"/>. For <see cref="UsageRange.All"/> this looks
        /// up the oldest event matching the filter's non-time constraints; other ranges need no query.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="filter">The filter whose non-time constraints scope the "all time" lookup. Null matches everything.</param>
        /// <param name="nowUnixMs">The current time in Unix epoch milliseconds.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The window.</returns>
        public async Task<UsageWindow> ResolveWindowAsync(UsageRange range, UsageFilter? filter, long nowUnixMs, CancellationToken token)
        {
            long earliest = range == UsageRange.All ? await GetEarliestEventUnixMsAsync(filter, token).ConfigureAwait(false) : 0;
            return UsageWindow.Compute(range, nowUnixMs, earliest);
        }

        /// <summary>
        /// Builds the token heatmap for a range: the shared clock-aligned <see cref="UsageHeatmapLayout"/> at the
        /// viewer's UTC offset (for <see cref="UsageRange.All"/> sized from the oldest event matching the filter's
        /// non-time constraints) and the tokens and calls per cell.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="filter">The non-time constraints (its window is replaced by the layout's). Null matches everything.</param>
        /// <param name="nowUnixMs">The current time in Unix epoch milliseconds.</param>
        /// <param name="utcOffsetMinutes">The viewer's offset from UTC in minutes, east positive.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The heatmap.</returns>
        public async Task<UsageHeatmap> GetHeatmapAsync(UsageRange range, UsageFilter? filter, long nowUnixMs, int utcOffsetMinutes, CancellationToken token)
        {
            long earliest = range == UsageRange.All ? await GetEarliestEventUnixMsAsync(filter, token).ConfigureAwait(false) : 0;
            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(range, nowUnixMs, utcOffsetMinutes, earliest);
            return await GetHeatmapAsync(layout, filter, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Fills a heatmap layout with the tokens and calls per cell. Cells start on local boundaries (local midnight,
        /// a Monday, a half-hour zone's hour), which the store's epoch-aligned buckets do not, so the rows are read at
        /// the largest width that divides both the cell width and the grid start, then summed into cells.
        /// </summary>
        /// <param name="layout">The layout. Required.</param>
        /// <param name="filter">The non-time constraints (its window is replaced by the layout's). Null matches everything.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The heatmap.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="layout"/> is null.</exception>
        public async Task<UsageHeatmap> GetHeatmapAsync(UsageHeatmapLayout layout, UsageFilter? filter, CancellationToken token)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            UsageFilter windowed = CloneWithSessionIds(filter, filter?.SessionIds);
            windowed.FromUnixMs = layout.FromUnixMs;
            windowed.ToUnixMs = layout.ToUnixMs - 1;
            UsageFilter? resolved = await ResolveFilterAsync(windowed, token).ConfigureAwait(false);
            long grain = Gcd(layout.BucketMs, Math.Abs(layout.FromUnixMs));
            List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(resolved, Math.Max(1, grain), token).ConfigureAwait(false);
            UsageHeatmap heatmap = new UsageHeatmap(layout);
            foreach (UsageAggregateRow row in rows)
            {
                heatmap.Add(row.BucketStartUnixMs, row.TotalTokens, row.Calls);
            }

            return heatmap;
        }

        /// <summary>
        /// Computes a breakdown of the window grouped by a dimension.
        /// </summary>
        /// <param name="dimension">One of "model", "endpoint", "provider", "command", "callkind", "label", "tag".</param>
        /// <param name="filter">The filter/window. Null covers everything.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The breakdown rows, descending by cost then total tokens.</returns>
        public async Task<List<UsageBreakdownRow>> GetBreakdownAsync(string dimension, UsageFilter? filter, CancellationToken token)
        {
            string dim = NormalizeDimension(dimension);

            if (dim == "label" || dim == "tag")
            {
                return await BreakdownByMetadataAsync(dim, filter, token).ConfigureAwait(false);
            }

            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            PricingTable pricing = _PricingProvider();
            List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(filter, 0, token).ConfigureAwait(false);

            Dictionary<string, List<UsageAggregateRow>> grouped = new Dictionary<string, List<UsageAggregateRow>>(StringComparer.Ordinal);
            foreach (UsageAggregateRow row in rows)
            {
                string key = KeyForDimension(dim, row);
                if (!grouped.TryGetValue(key, out List<UsageAggregateRow>? list))
                {
                    list = new List<UsageAggregateRow>();
                    grouped[key] = list;
                }

                list.Add(row);
            }

            List<UsageBreakdownRow> result = new List<UsageBreakdownRow>();
            foreach (KeyValuePair<string, List<UsageAggregateRow>> entry in grouped)
            {
                result.Add(new UsageBreakdownRow
                {
                    Dimension = dim,
                    Value = entry.Key,
                    Metrics = RollUp(entry.Value, pricing)
                });
            }

            return result
                .OrderByDescending(r => r.Metrics.CostUsd)
                .ThenByDescending(r => r.Metrics.TotalTokens)
                .ToList();
        }

        /// <summary>
        /// Fetches a page of raw usage events with derived cost.
        /// </summary>
        /// <param name="filter">The filter. Null matches everything.</param>
        /// <param name="pageNumber">The 1-based page number.</param>
        /// <param name="pageSize">The page size (clamped 1-500 by the store).</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The page.</returns>
        public async Task<UsageEventPage> GetEventsAsync(UsageFilter? filter, int pageNumber, int pageSize, CancellationToken token)
        {
            filter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            PricingTable pricing = _PricingProvider();
            List<UsageEventRow> rows = await _Store.QueryEventsAsync(filter, pageNumber, pageSize, token).ConfigureAwait(false);
            long total = await _Store.CountEventsAsync(filter, token).ConfigureAwait(false);

            foreach (UsageEventRow row in rows)
            {
                row.CostUsd = pricing.ComputeCostUsd(row.Model, row.InputTokens, row.CachedTokens, row.OutputTokens);
            }

            return new UsageEventPage
            {
                Items = rows,
                TotalCount = total,
                PageNumber = pageNumber < 1 ? 1 : pageNumber,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// Deletes a single usage event by id.
        /// </summary>
        /// <param name="id">The row id to delete.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The number of rows deleted (0 or 1).</returns>
        public Task<int> DeleteEventAsync(long id, CancellationToken token)
        {
            return _Store.DeleteEventAsync(id, token);
        }

        /// <summary>
        /// Returns the distinct endpoint names seen in the store (for filter controls).
        /// </summary>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The endpoint names, ascending.</returns>
        public Task<List<string>> GetEndpointsAsync(CancellationToken token)
        {
            return _Store.DistinctValuesAsync("endpoint_name", token);
        }

        /// <summary>
        /// Returns the distinct models seen in the store (for filter controls).
        /// </summary>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The models, ascending.</returns>
        public Task<List<string>> GetModelsAsync(CancellationToken token)
        {
            return _Store.DistinctValuesAsync("model", token);
        }

        /// <summary>
        /// Returns the distinct labels across all sessions (for filter controls), ascending. Empty when no
        /// session metadata index is configured.
        /// </summary>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The distinct labels.</returns>
        public async Task<List<string>> GetLabelsAsync(CancellationToken token)
        {
            if (_SessionIndex == null)
            {
                return new List<string>();
            }

            IReadOnlyList<SessionMetadataEntry> entries = await _SessionIndex.SnapshotAsync(token).ConfigureAwait(false);
            return entries
                .SelectMany(e => e.Labels)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Returns the distinct key/value tags across all sessions (for filter controls), ordered by key then
        /// value. Empty when no session metadata index is configured.
        /// </summary>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The distinct tags.</returns>
        public async Task<List<SessionTag>> GetTagsAsync(CancellationToken token)
        {
            if (_SessionIndex == null)
            {
                return new List<SessionTag>();
            }

            IReadOnlyList<SessionMetadataEntry> entries = await _SessionIndex.SnapshotAsync(token).ConfigureAwait(false);
            Dictionary<string, SessionTag> distinct = new Dictionary<string, SessionTag>(StringComparer.OrdinalIgnoreCase);
            foreach (SessionMetadataEntry entry in entries)
            {
                foreach (SessionTag tag in entry.Tags)
                {
                    string key = tag.Key + " " + tag.Value;
                    if (!distinct.ContainsKey(key))
                    {
                        distinct[key] = new SessionTag(tag.Key, tag.Value);
                    }
                }
            }

            return distinct.Values
                .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        #endregion

        #region Private-Methods

        // Resolves a filter's label/tag constraints into a concrete session-id set (an AND across every
        // requested label and tag). Returns the filter unchanged when it carries no label/tag constraint;
        // otherwise returns a copy with SessionIds populated (empty when nothing matched, which yields zero
        // rows). When no session index is configured, a label/tag constraint matches nothing.
        private async Task<UsageFilter?> ResolveFilterAsync(UsageFilter? filter, CancellationToken token)
        {
            if (filter == null)
            {
                return null;
            }

            bool hasLabels = filter.Labels != null && filter.Labels.Count > 0;
            bool hasTags = filter.Tags != null && filter.Tags.Count > 0;
            if (!hasLabels && !hasTags)
            {
                return filter;
            }

            HashSet<string> matched = new HashSet<string>(StringComparer.Ordinal);
            if (_SessionIndex != null)
            {
                IReadOnlyList<SessionMetadataEntry> entries = await _SessionIndex.SnapshotAsync(token).ConfigureAwait(false);
                foreach (SessionMetadataEntry entry in entries)
                {
                    if (MatchesMetadata(entry, filter.Labels, filter.Tags))
                    {
                        matched.Add(entry.Id);
                    }
                }
            }

            IReadOnlyCollection<string> resolved = matched;
            if (filter.SessionIds != null)
            {
                // Compose with any session set already on the filter (intersection).
                resolved = matched.Where(filter.SessionIds.Contains).ToList();
            }

            return CloneWithSessionIds(filter, resolved);
        }

        // True when the entry carries every requested label (case-insensitive) and every requested tag
        // (key case-insensitive, value case-insensitive).
        private static bool MatchesMetadata(SessionMetadataEntry entry, List<string>? labels, List<SessionTag>? tags)
        {
            if (labels != null)
            {
                foreach (string label in labels)
                {
                    if (string.IsNullOrWhiteSpace(label)) continue;
                    if (!entry.Labels.Any(l => string.Equals(l, label, StringComparison.OrdinalIgnoreCase)))
                    {
                        return false;
                    }
                }
            }

            if (tags != null)
            {
                foreach (SessionTag tag in tags)
                {
                    if (tag == null || string.IsNullOrWhiteSpace(tag.Key)) continue;
                    bool present = entry.Tags.Any(t =>
                        string.Equals(t.Key, tag.Key, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(t.Value, tag.Value, StringComparison.OrdinalIgnoreCase));
                    if (!present)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        // Breakdown grouped by label or by key:value tag. Because usage rows carry no metadata, each distinct
        // value's session set is resolved from the index and summarized with a per-value session filter. A
        // session with N labels contributes to all N label buckets (intentional fan-out — per-label totals can
        // exceed the grand total); this is documented and asserted in tests.
        private async Task<List<UsageBreakdownRow>> BreakdownByMetadataAsync(string dim, UsageFilter? filter, CancellationToken token)
        {
            UsageFilter? baseFilter = await ResolveFilterAsync(filter, token).ConfigureAwait(false);
            IReadOnlyCollection<string>? baseSessions = baseFilter?.SessionIds;
            PricingTable pricing = _PricingProvider();

            List<UsageBreakdownRow> result = new List<UsageBreakdownRow>();
            if (_SessionIndex == null)
            {
                return result;
            }

            IReadOnlyList<SessionMetadataEntry> entries = await _SessionIndex.SnapshotAsync(token).ConfigureAwait(false);

            // value -> set of session ids carrying it.
            Dictionary<string, HashSet<string>> valueToSessions = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (SessionMetadataEntry entry in entries)
            {
                if (dim == "label")
                {
                    foreach (string label in entry.Labels)
                    {
                        AddToBucket(valueToSessions, label, entry.Id);
                    }
                }
                else
                {
                    foreach (SessionTag tag in entry.Tags)
                    {
                        AddToBucket(valueToSessions, tag.Key + ": " + tag.Value, entry.Id);
                    }
                }
            }

            foreach (KeyValuePair<string, HashSet<string>> pair in valueToSessions)
            {
                IEnumerable<string> sessionIds = pair.Value;
                if (baseSessions != null)
                {
                    sessionIds = sessionIds.Where(baseSessions.Contains);
                }

                List<string> ids = sessionIds.ToList();
                UsageFilter perValue = CloneWithSessionIds(filter, ids);
                perValue.Labels = null;
                perValue.Tags = null;

                List<UsageAggregateRow> rows = await _Store.FetchAggregatesAsync(perValue, 0, token).ConfigureAwait(false);
                result.Add(new UsageBreakdownRow
                {
                    Dimension = dim,
                    Value = pair.Key,
                    Metrics = RollUp(rows, pricing)
                });
            }

            return result
                .OrderByDescending(r => r.Metrics.CostUsd)
                .ThenByDescending(r => r.Metrics.TotalTokens)
                .ToList();
        }

        private static void AddToBucket(Dictionary<string, HashSet<string>> map, string key, string sessionId)
        {
            if (!map.TryGetValue(key, out HashSet<string>? set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                map[key] = set;
            }

            set.Add(sessionId);
        }

        private static UsageFilter CloneWithSessionIds(UsageFilter? source, IReadOnlyCollection<string>? sessionIds)
        {
            return new UsageFilter
            {
                FromUnixMs = source?.FromUnixMs ?? 0,
                ToUnixMs = source?.ToUnixMs ?? 0,
                EndpointName = source?.EndpointName,
                Model = source?.Model,
                SessionId = source?.SessionId,
                CallKind = source?.CallKind,
                Success = source?.Success,
                Labels = source?.Labels,
                Tags = source?.Tags,
                SessionIds = sessionIds
            };
        }

        private static long Gcd(long a, long b)
        {
            while (b != 0)
            {
                long t = a % b;
                a = b;
                b = t;
            }

            return a;
        }

        private static UsageMetrics RollUp(IEnumerable<UsageAggregateRow> rows, PricingTable pricing)
        {
            UsageMetrics metrics = new UsageMetrics();

            long ttftSum = 0;
            long ttftCount = 0;
            long totalMsSum = 0;
            long totalMsCount = 0;
            long streamSum = 0;
            long streamCount = 0;
            double tpsSum = 0;
            long tpsCount = 0;

            foreach (UsageAggregateRow row in rows)
            {
                metrics.Calls += row.Calls;
                metrics.Errors += row.Errors;
                metrics.InputTokens += row.InputTokens;
                metrics.CachedTokens += row.CachedTokens;
                metrics.OutputTokens += row.OutputTokens;
                metrics.TotalTokens += row.TotalTokens;
                metrics.CostUsd += pricing.ComputeCostUsd(row.Model, row.InputTokens, row.CachedTokens, row.OutputTokens);

                ttftSum += row.TtftSumMs;
                ttftCount += row.TtftCount;
                totalMsSum += row.TotalMsSum;
                totalMsCount += row.TotalMsCount;
                streamSum += row.StreamMsSum;
                streamCount += row.StreamMsCount;
                tpsSum += row.TokensPerSecSum;
                tpsCount += row.TokensPerSecCount;
            }

            metrics.ErrorRate = metrics.Calls > 0 ? (double)metrics.Errors / metrics.Calls : 0.0;
            long promptTotal = metrics.InputTokens + metrics.CachedTokens;
            metrics.CacheHitRate = promptTotal > 0 ? (double)metrics.CachedTokens / promptTotal : 0.0;
            metrics.AvgTtftMs = ttftCount > 0 ? (double)ttftSum / ttftCount : 0.0;
            metrics.AvgTotalMs = totalMsCount > 0 ? (double)totalMsSum / totalMsCount : 0.0;
            metrics.AvgStreamMs = streamCount > 0 ? (double)streamSum / streamCount : 0.0;
            metrics.AvgTokensPerSec = tpsCount > 0 ? tpsSum / tpsCount : 0.0;

            return metrics;
        }

        private static void ApplyPercentiles(UsageMetrics metrics, List<UsageLatencySample> samples)
        {
            List<double> ttft = new List<double>();
            List<double> total = new List<double>();
            List<double> stream = new List<double>();
            List<double> throughput = new List<double>();

            foreach (UsageLatencySample sample in samples)
            {
                if (sample.TimeToFirstTokenMs.HasValue)
                {
                    ttft.Add(sample.TimeToFirstTokenMs.Value);
                }

                if (sample.TotalMs.HasValue)
                {
                    total.Add(sample.TotalMs.Value);
                }

                if (sample.StreamMs.HasValue)
                {
                    stream.Add(sample.StreamMs.Value);
                }

                if (sample.ThroughputPerSec.HasValue)
                {
                    throughput.Add(sample.ThroughputPerSec.Value);
                }
            }

            // Build the five-number summaries (each sorts its list in place).
            metrics.TtftMsDist = UsageDistribution.From(ttft, Percentile);
            metrics.TotalMsDist = UsageDistribution.From(total, Percentile);
            metrics.StreamMsDist = UsageDistribution.From(stream, Percentile);
            metrics.ThroughputDist = UsageDistribution.From(throughput, Percentile);

            metrics.P50TtftMs = Percentile(ttft, 0.50);
            metrics.P95TtftMs = metrics.TtftMsDist.P95;
            metrics.P99TtftMs = metrics.TtftMsDist.P99;
            metrics.P50TotalMs = Percentile(total, 0.50);
            metrics.P95TotalMs = metrics.TotalMsDist.P95;
            metrics.P99TotalMs = metrics.TotalMsDist.P99;

            // Prefer the sample-derived streaming/throughput means so the KPI cards match the recomputed,
            // corrected throughput (the roll-up's AvgTokensPerSec came from the stale stored column).
            if (metrics.StreamMsDist.Count > 0)
            {
                metrics.AvgStreamMs = metrics.StreamMsDist.Avg;
            }

            if (metrics.ThroughputDist.Count > 0)
            {
                metrics.AvgTokensPerSec = metrics.ThroughputDist.Avg;
            }
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0)
            {
                return 0.0;
            }

            if (sorted.Count == 1)
            {
                return sorted[0];
            }

            // Linear interpolation between closest ranks on the already-sorted list.
            double rank = p * (sorted.Count - 1);
            int low = (int)Math.Floor(rank);
            int high = (int)Math.Ceiling(rank);
            if (low == high)
            {
                return sorted[low];
            }

            double weight = rank - low;
            return (sorted[low] * (1.0 - weight)) + (sorted[high] * weight);
        }

        private static string NormalizeDimension(string? dimension)
        {
            switch ((dimension ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "endpoint":
                    return "endpoint";
                case "provider":
                case "adapter":
                    return "provider";
                case "command":
                    return "command";
                case "callkind":
                case "call_kind":
                    return "callkind";
                case "label":
                    return "label";
                case "tag":
                    return "tag";
                case "model":
                default:
                    return "model";
            }
        }

        private static string KeyForDimension(string dimension, UsageAggregateRow row)
        {
            switch (dimension)
            {
                case "endpoint":
                    return string.IsNullOrEmpty(row.EndpointName) ? "unknown" : row.EndpointName;
                case "provider":
                    return string.IsNullOrEmpty(row.AdapterType) ? "unknown" : row.AdapterType;
                case "command":
                    return string.IsNullOrEmpty(row.Command) ? "unknown" : row.Command;
                case "callkind":
                    return string.IsNullOrEmpty(row.CallKind) ? "unknown" : row.CallKind;
                case "model":
                default:
                    return string.IsNullOrEmpty(row.Model) ? "unknown" : row.Model;
            }
        }

        #endregion
    }
}
