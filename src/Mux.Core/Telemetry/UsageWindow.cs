namespace Mux.Core.Telemetry
{
    using System;

    /// <summary>
    /// A resolved time window and bucket granularity for a <see cref="UsageRange"/>: the inclusive lower and
    /// exclusive upper bounds (Unix epoch milliseconds), the bucket size, and the bucket count. The window is
    /// snapped to the bucket grid so successive refreshes align. The static helpers (<see cref="Bucketing"/>,
    /// <see cref="NaturalSpanMs"/>, <see cref="NiceBucketMs"/>) are the single source of the range→grid,
    /// range→span, and custom-range bucket tables shared by the desktop analytics view and the mux serve
    /// dashboard so the two surfaces bucket identically (hour 60×1-min, day 96×15-min, week 84×2-hour,
    /// month 60×12-hour).
    /// </summary>
    public sealed class UsageWindow
    {
        private const long MinuteMs = 60_000L;

        private UsageWindow(long fromUnixMs, long toUnixMs, long bucketMs, int bucketCount)
        {
            _FromUnixMs = fromUnixMs;
            _ToUnixMs = toUnixMs;
            _BucketMs = bucketMs;
            _BucketCount = bucketCount;
        }

        private readonly long _FromUnixMs;
        private readonly long _ToUnixMs;
        private readonly long _BucketMs;
        private readonly int _BucketCount;

        /// <summary>The inclusive lower bound of the window, Unix epoch milliseconds.</summary>
        public long FromUnixMs
        {
            get => _FromUnixMs;
        }

        /// <summary>The exclusive upper bound of the window, Unix epoch milliseconds.</summary>
        public long ToUnixMs
        {
            get => _ToUnixMs;
        }

        /// <summary>The bucket size in milliseconds.</summary>
        public long BucketMs
        {
            get => _BucketMs;
        }

        /// <summary>The number of buckets spanning the window.</summary>
        public int BucketCount
        {
            get => _BucketCount;
        }

        /// <summary>
        /// Resolve the window and bucketing for a range relative to a reference "now". The upper bound is
        /// snapped up to the next bucket boundary and the lower bound is <c>count × bucket</c> earlier, so the
        /// grid is stable across refreshes.
        /// </summary>
        /// <param name="range">The selected range.</param>
        /// <param name="nowUnixMs">The reference time, Unix epoch milliseconds.</param>
        /// <returns>The resolved window.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="range"/> is not a known value.</exception>
        public static UsageWindow Compute(UsageRange range, long nowUnixMs)
        {
            if (range != UsageRange.Hour && range != UsageRange.Day && range != UsageRange.Week && range != UsageRange.Month)
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Unknown usage range.");
            }

            Bucketing(RangeToKey(range), out long bucketMs, out int count);

            // Snap the upper bound up to the next bucket boundary so buckets align to a stable grid.
            long to = ((nowUnixMs + bucketMs - 1) / bucketMs) * bucketMs;
            long from = to - ((long)count * bucketMs);
            return new UsageWindow(from, to, bucketMs, count);
        }

        /// <summary>
        /// The timeseries bucket size and count for a range keyword (hour/day/week/month). Unrecognized
        /// values fall back to the day grid (96 × 15 minutes).
        /// </summary>
        /// <param name="range">The range keyword (case-insensitive), or null.</param>
        /// <param name="bucketMs">The bucket size in milliseconds.</param>
        /// <param name="count">The number of buckets.</param>
        public static void Bucketing(string? range, out long bucketMs, out int count)
        {
            switch ((range ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "hour":
                    bucketMs = MinuteMs;
                    count = 60;
                    break;
                case "week":
                    bucketMs = 120L * MinuteMs;
                    count = 84;
                    break;
                case "month":
                    bucketMs = 720L * MinuteMs;
                    count = 60;
                    break;
                case "day":
                default:
                    bucketMs = 15L * MinuteMs;
                    count = 96;
                    break;
            }
        }

        /// <summary>
        /// The natural span in milliseconds for a range keyword (hour = 1h, day = 24h, week = 7d, month = 30d);
        /// "all" and unrecognized values return 0 (no window).
        /// </summary>
        /// <param name="range">The range keyword (case-insensitive), or null.</param>
        /// <returns>The span in milliseconds, or 0.</returns>
        public static long NaturalSpanMs(string? range)
        {
            switch ((range ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "hour": return 60L * 60L * 1000L;
                case "day": return 24L * 60L * 60L * 1000L;
                case "week": return 7L * 24L * 60L * 60L * 1000L;
                case "month": return 30L * 24L * 60L * 60L * 1000L;
                default: return 0L;
            }
        }

        /// <summary>
        /// Rounds a target bucket size up to the nearest "nice" bucket (1m, 5m, 15m, 30m, 1h, 2h, 6h, 12h, 24h),
        /// used to pick a bucket for an arbitrary custom time window.
        /// </summary>
        /// <param name="target">The desired bucket size in milliseconds.</param>
        /// <returns>The chosen nice bucket size in milliseconds.</returns>
        public static long NiceBucketMs(long target)
        {
            long[] steps = new long[]
            {
                60L * 1000L, 5L * 60L * 1000L, 15L * 60L * 1000L, 30L * 60L * 1000L,
                60L * 60L * 1000L, 2L * 60L * 60L * 1000L, 6L * 60L * 60L * 1000L,
                12L * 60L * 60L * 1000L, 24L * 60L * 60L * 1000L
            };

            foreach (long step in steps)
            {
                if (target <= step)
                {
                    return step;
                }
            }

            return steps[steps.Length - 1];
        }

        private static string RangeToKey(UsageRange range)
        {
            switch (range)
            {
                case UsageRange.Hour: return "hour";
                case UsageRange.Week: return "week";
                case UsageRange.Month: return "month";
                default: return "day";
            }
        }
    }
}
