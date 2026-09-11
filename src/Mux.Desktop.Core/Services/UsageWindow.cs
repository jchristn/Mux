namespace Mux.Desktop.Services
{
    using System;

    /// <summary>
    /// A resolved time window and bucket granularity for a <see cref="UsageRange"/>: the inclusive lower and
    /// exclusive upper bounds (Unix epoch milliseconds), the bucket size, and the bucket count. The window is
    /// snapped to the bucket grid so successive refreshes align, matching the dashboard's server-side
    /// bucketing (hour 60×1-min, day 96×15-min, week 84×2-hour, month 60×12-hour).
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
            long bucketMs;
            int count;

            switch (range)
            {
                case UsageRange.Hour:
                    bucketMs = MinuteMs;
                    count = 60;
                    break;
                case UsageRange.Day:
                    bucketMs = 15L * MinuteMs;
                    count = 96;
                    break;
                case UsageRange.Week:
                    bucketMs = 120L * MinuteMs;
                    count = 84;
                    break;
                case UsageRange.Month:
                    bucketMs = 720L * MinuteMs;
                    count = 60;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(range), range, "Unknown usage range.");
            }

            // Snap the upper bound up to the next bucket boundary so buckets align to a stable grid.
            long to = ((nowUnixMs + bucketMs - 1) / bucketMs) * bucketMs;
            long from = to - ((long)count * bucketMs);
            return new UsageWindow(from, to, bucketMs, count);
        }
    }
}
