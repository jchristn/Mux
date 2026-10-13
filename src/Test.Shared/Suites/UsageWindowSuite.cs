namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="UsageWindow.Compute"/>: the range → (bucket size, count, window)
    /// mapping that mirrors the mux serve dashboard, grid snapping, and unknown-range rejection.
    /// </summary>
    public static class UsageWindowSuite
    {
        private const long MinuteMs = 60_000L;

        /// <summary>
        /// Builds the usage-window suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the usage-window cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "UsageWindow",
                "Usage range window mapping",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("UsageWindow", "HourBuckets", "Hour is 60 x 1-minute", (CancellationToken ct) =>
                    {
                        UsageWindow window = UsageWindow.Compute(UsageRange.Hour, 1_000_000_000L);
                        MuxAssert.AreEqual(MinuteMs, window.BucketMs, "hour bucket");
                        MuxAssert.AreEqual(60, window.BucketCount, "hour count");
                        MuxAssert.AreEqual(60L * MinuteMs, window.ToUnixMs - window.FromUnixMs, "hour span");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "DayBuckets", "Day is 96 x 15-minute", (CancellationToken ct) =>
                    {
                        UsageWindow window = UsageWindow.Compute(UsageRange.Day, 1_000_000_000L);
                        MuxAssert.AreEqual(15L * MinuteMs, window.BucketMs, "day bucket");
                        MuxAssert.AreEqual(96, window.BucketCount, "day count");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "WeekBuckets", "Week is 84 x 2-hour", (CancellationToken ct) =>
                    {
                        UsageWindow window = UsageWindow.Compute(UsageRange.Week, 1_000_000_000L);
                        MuxAssert.AreEqual(120L * MinuteMs, window.BucketMs, "week bucket");
                        MuxAssert.AreEqual(84, window.BucketCount, "week count");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "MonthBuckets", "Month is 60 x 12-hour", (CancellationToken ct) =>
                    {
                        UsageWindow window = UsageWindow.Compute(UsageRange.Month, 1_000_000_000L);
                        MuxAssert.AreEqual(720L * MinuteMs, window.BucketMs, "month bucket");
                        MuxAssert.AreEqual(60, window.BucketCount, "month count");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "SnapsUpperBound", "Upper bound snaps to the bucket grid at or after now", (CancellationToken ct) =>
                    {
                        long now = 1_234_567_891L;
                        UsageWindow window = UsageWindow.Compute(UsageRange.Hour, now);
                        MuxAssert.AreEqual(0L, window.ToUnixMs % MinuteMs, "snapped to grid");
                        MuxAssert.IsTrue(window.ToUnixMs >= now, "at or after now");
                        MuxAssert.IsTrue(window.ToUnixMs - now < MinuteMs, "within one bucket of now");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapRowsAreClockAligned", "Heatmap windows end on clock row boundaries while preserving fixed bucket counts", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 10, 13, 12, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageRange[] ranges = { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month };
                        long[] rowWidths = { 15 * MinuteMs, 60 * MinuteMs, 1440 * MinuteMs, 1440 * MinuteMs };
                        int[] counts = { 60, 96, 84, 60 };
                        for (int i = 0; i < ranges.Length; i++)
                        {
                            UsageWindow window = UsageWindow.ComputeHeatmap(ranges[i], now);
                            MuxAssert.AreEqual(0L, window.FromUnixMs % rowWidths[i], "start anchored for " + ranges[i]);
                            MuxAssert.AreEqual(0L, window.ToUnixMs % rowWidths[i], "end anchored for " + ranges[i]);
                            MuxAssert.AreEqual(counts[i], window.BucketCount, "fixed spots for " + ranges[i]);
                            MuxAssert.IsTrue(window.FromUnixMs <= now && now < window.ToUnixMs, "current partial row included");
                            UsageWindow sameRow = UsageWindow.ComputeHeatmap(ranges[i], now + MinuteMs);
                            MuxAssert.AreEqual(window.FromUnixMs, sameRow.FromUnixMs, "axes stable within clock row");
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "UnknownRangeThrows", "An unknown range is rejected", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<ArgumentOutOfRangeException>(() => UsageWindow.Compute((UsageRange)999, 0L), "unknown range");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
