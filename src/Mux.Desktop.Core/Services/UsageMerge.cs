namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Combines results of the same query run once per endpoint, for when the desktop's endpoint text filter
    /// matches more than one endpoint (the query service filters by a single exact endpoint name). Counts,
    /// tokens, cost, minimums, maximums, and averages combine exactly. Percentiles (p50/p95/p99) cannot be
    /// recombined from per-endpoint values, so they are count-weighted means of the parts: an approximation
    /// used only in the multi-endpoint case.
    /// </summary>
    public static class UsageMerge
    {
        #region Public-Methods

        /// <summary>
        /// Combine several metric sets into one.
        /// </summary>
        /// <param name="parts">The metric sets. Null entries are skipped.</param>
        /// <returns>The combined metrics.</returns>
        public static UsageMetrics MergeMetrics(IEnumerable<UsageMetrics?>? parts)
        {
            List<UsageMetrics> list = (parts ?? Enumerable.Empty<UsageMetrics?>()).Where(p => p != null).Select(p => p!).ToList();
            UsageMetrics merged = new UsageMetrics();
            if (list.Count == 1)
            {
                return list[0];
            }

            foreach (UsageMetrics m in list)
            {
                merged.Calls += m.Calls;
                merged.Errors += m.Errors;
                merged.InputTokens += m.InputTokens;
                merged.CachedTokens += m.CachedTokens;
                merged.OutputTokens += m.OutputTokens;
                merged.TotalTokens += m.TotalTokens;
                merged.CostUsd += m.CostUsd;
            }

            merged.ErrorRate = merged.Calls > 0 ? (double)merged.Errors / merged.Calls : 0.0;
            merged.CacheHitRate = merged.InputTokens > 0 ? (double)merged.CachedTokens / merged.InputTokens : 0.0;

            merged.TtftMsDist = MergeDistributions(list.Select(m => m.TtftMsDist));
            merged.TotalMsDist = MergeDistributions(list.Select(m => m.TotalMsDist));
            merged.StreamMsDist = MergeDistributions(list.Select(m => m.StreamMsDist));
            merged.ThroughputDist = MergeDistributions(list.Select(m => m.ThroughputDist));

            merged.AvgTtftMs = WeightedMean(list, m => m.AvgTtftMs, m => m.TtftMsDist.Count);
            merged.P50TtftMs = WeightedMean(list, m => m.P50TtftMs, m => m.TtftMsDist.Count);
            merged.P95TtftMs = WeightedMean(list, m => m.P95TtftMs, m => m.TtftMsDist.Count);
            merged.P99TtftMs = WeightedMean(list, m => m.P99TtftMs, m => m.TtftMsDist.Count);
            merged.AvgTotalMs = WeightedMean(list, m => m.AvgTotalMs, m => m.TotalMsDist.Count);
            merged.P50TotalMs = WeightedMean(list, m => m.P50TotalMs, m => m.TotalMsDist.Count);
            merged.P95TotalMs = WeightedMean(list, m => m.P95TotalMs, m => m.TotalMsDist.Count);
            merged.P99TotalMs = WeightedMean(list, m => m.P99TotalMs, m => m.TotalMsDist.Count);
            merged.AvgStreamMs = WeightedMean(list, m => m.AvgStreamMs, m => m.StreamMsDist.Count);
            merged.AvgTokensPerSec = WeightedMean(list, m => m.AvgTokensPerSec, m => m.ThroughputDist.Count);
            return merged;
        }

        /// <summary>
        /// Combine distributions: exact min, max, count, and mean; count-weighted p95/p99.
        /// </summary>
        /// <param name="parts">The distributions. Null and empty entries are skipped.</param>
        /// <returns>The combined distribution (empty when every part is empty).</returns>
        public static UsageDistribution MergeDistributions(IEnumerable<UsageDistribution?>? parts)
        {
            List<UsageDistribution> list = (parts ?? Enumerable.Empty<UsageDistribution?>())
                .Where(d => d != null && d.Count > 0).Select(d => d!).ToList();
            UsageDistribution merged = new UsageDistribution();
            if (list.Count == 0)
            {
                return merged;
            }

            long count = list.Sum(d => d.Count);
            merged.Count = count;
            merged.Min = list.Min(d => d.Min);
            merged.Max = list.Max(d => d.Max);
            merged.Avg = list.Sum(d => d.Avg * d.Count) / count;
            merged.P95 = list.Sum(d => d.P95 * d.Count) / count;
            merged.P99 = list.Sum(d => d.P99 * d.Count) / count;
            return merged;
        }

        /// <summary>
        /// Combine dense time series that share one bucket grid, bucket by bucket.
        /// </summary>
        /// <param name="parts">The series. Null entries are skipped.</param>
        /// <returns>The combined buckets, ascending by start.</returns>
        public static List<UsageBucket> MergeBuckets(IEnumerable<List<UsageBucket>?>? parts)
        {
            List<List<UsageBucket>> list = (parts ?? Enumerable.Empty<List<UsageBucket>?>()).Where(p => p != null).Select(p => p!).ToList();
            if (list.Count == 1)
            {
                return list[0];
            }

            return list
                .SelectMany(p => p)
                .GroupBy(b => b.BucketStartUnixMs)
                .OrderBy(g => g.Key)
                .Select(g => new UsageBucket { BucketStartUnixMs = g.Key, Metrics = MergeMetrics(g.Select(b => b.Metrics)) })
                .ToList();
        }

        /// <summary>
        /// Combine split time series by key, then re-rank by total tokens (ties by cost, then key) and fold
        /// everything past <paramref name="top"/> into "Other". A part's own "Other" series is folded into
        /// the combined "Other". The fold happens only when it leaves at least two values (or any part
        /// already had an "Other"), matching the query service.
        /// </summary>
        /// <param name="parts">Each part's series (ideally queried with a generous top so little is pre-folded).</param>
        /// <param name="top">How many named values to keep.</param>
        /// <returns>The combined, ranked series with "Other" (when present) last.</returns>
        public static List<UsageSeries> MergeSplit(IEnumerable<List<UsageSeries>?>? parts, int top)
        {
            List<List<UsageSeries>> list = (parts ?? Enumerable.Empty<List<UsageSeries>?>()).Where(p => p != null).Select(p => p!).ToList();
            int keep = Math.Max(1, top);

            Dictionary<string, List<UsageSeries>> named = new Dictionary<string, List<UsageSeries>>(StringComparer.Ordinal);
            List<UsageSeries> others = new List<UsageSeries>();
            foreach (UsageSeries series in list.SelectMany(p => p))
            {
                if (series.IsOther)
                {
                    others.Add(series);
                    continue;
                }

                if (!named.TryGetValue(series.Key, out List<UsageSeries>? group))
                {
                    group = new List<UsageSeries>();
                    named[series.Key] = group;
                }

                group.Add(series);
            }

            List<UsageSeries> combined = named
                .Select(kv => Combine(kv.Key, false, kv.Value))
                .OrderByDescending(s => s.Buckets.Sum(b => b.Metrics.TotalTokens))
                .ThenByDescending(s => s.Buckets.Sum(b => b.Metrics.CostUsd))
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .ToList();

            bool fold = combined.Count > keep + 1 || (others.Count > 0 && combined.Count > keep);
            List<UsageSeries> result = new List<UsageSeries>();
            List<UsageSeries> folded = new List<UsageSeries>(others);
            for (int i = 0; i < combined.Count; i++)
            {
                if (fold && i >= keep)
                {
                    folded.Add(combined[i]);
                }
                else
                {
                    result.Add(combined[i]);
                }
            }

            if (folded.Count > 0)
            {
                result.Add(Combine("Other", true, folded));
            }

            return result;
        }

        /// <summary>
        /// Combine failure counts by code, most frequent first (ties by code).
        /// </summary>
        /// <param name="parts">Each part's counts. Null entries are skipped.</param>
        /// <returns>The combined counts.</returns>
        public static List<UsageErrorCount> MergeErrors(IEnumerable<List<UsageErrorCount>?>? parts)
        {
            return (parts ?? Enumerable.Empty<List<UsageErrorCount>?>())
                .Where(p => p != null)
                .SelectMany(p => p!)
                .GroupBy(e => e.ErrorCode, StringComparer.Ordinal)
                .Select(g => new UsageErrorCount { ErrorCode = g.Key, Count = g.Sum(e => e.Count) })
                .OrderByDescending(e => e.Count)
                .ThenBy(e => e.ErrorCode, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Combine breakdown rows by value, sorted by cost then total tokens (both descending).
        /// </summary>
        /// <param name="parts">Each part's rows. Null entries are skipped.</param>
        /// <returns>The combined rows.</returns>
        public static List<UsageBreakdownRow> MergeBreakdown(IEnumerable<List<UsageBreakdownRow>?>? parts)
        {
            return (parts ?? Enumerable.Empty<List<UsageBreakdownRow>?>())
                .Where(p => p != null)
                .SelectMany(p => p!)
                .GroupBy(r => r.Value, StringComparer.Ordinal)
                .Select(g => new UsageBreakdownRow { Dimension = g.First().Dimension, Value = g.Key, Metrics = MergeMetrics(g.Select(r => r.Metrics)) })
                .OrderByDescending(r => r.Metrics.CostUsd)
                .ThenByDescending(r => r.Metrics.TotalTokens)
                .ThenBy(r => r.Value, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Combine first pages of events (each newest first) into one newest-first page of at most
        /// <paramref name="pageSize"/> rows, with the summed total count.
        /// </summary>
        /// <param name="parts">Each part's first page. Null entries are skipped.</param>
        /// <param name="pageSize">The page size.</param>
        /// <returns>The combined page.</returns>
        public static UsageEventPage MergeEventPages(IEnumerable<UsageEventPage?>? parts, int pageSize)
        {
            List<UsageEventPage> list = (parts ?? Enumerable.Empty<UsageEventPage?>()).Where(p => p != null).Select(p => p!).ToList();
            int size = Math.Max(1, pageSize);
            return new UsageEventPage
            {
                Items = list
                    .SelectMany(p => p.Items)
                    .OrderByDescending(r => r.TimestampUnixMs)
                    .ThenByDescending(r => r.Id)
                    .Take(size)
                    .ToList(),
                TotalCount = list.Sum(p => p.TotalCount),
                PageNumber = 1,
                PageSize = size
            };
        }

        #endregion

        #region Private-Methods

        private static UsageSeries Combine(string key, bool isOther, List<UsageSeries> group)
        {
            return new UsageSeries
            {
                Key = key,
                IsOther = isOther,
                Buckets = MergeBuckets(group.Select(s => s.Buckets))
            };
        }

        private static double WeightedMean(List<UsageMetrics> parts, Func<UsageMetrics, double> value, Func<UsageMetrics, long> weight)
        {
            double sum = 0;
            long total = 0;
            foreach (UsageMetrics m in parts)
            {
                long w = weight(m);
                if (w <= 0)
                {
                    continue;
                }

                sum += value(m) * w;
                total += w;
            }

            return total > 0 ? sum / total : 0.0;
        }

        #endregion
    }
}
