namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

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

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageQueryService"/> class.
        /// </summary>
        /// <param name="store">The backing store. Required.</param>
        /// <param name="pricingProvider">A provider of the current pricing table. Required; called per query.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public UsageQueryService(SqliteUsageStore store, Func<PricingTable> pricingProvider)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _PricingProvider = pricingProvider ?? throw new ArgumentNullException(nameof(pricingProvider));
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

        /// <summary>
        /// Computes a breakdown of the window grouped by a dimension.
        /// </summary>
        /// <param name="dimension">One of "model", "endpoint", "provider", "command".</param>
        /// <param name="filter">The filter/window. Null covers everything.</param>
        /// <param name="token">A token to cancel the operation.</param>
        /// <returns>The breakdown rows, descending by cost then total tokens.</returns>
        public async Task<List<UsageBreakdownRow>> GetBreakdownAsync(string dimension, UsageFilter? filter, CancellationToken token)
        {
            string dim = NormalizeDimension(dimension);
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

        #endregion

        #region Private-Methods

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
