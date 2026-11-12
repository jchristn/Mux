namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="UsageWindow.Compute(UsageRange, long)"/>: the range → (bucket size, count, window)
    /// mapping that mirrors the mux serve dashboard, grid snapping, and unknown-range rejection.
    /// </summary>
    public static class UsageWindowSuite
    {
        private const long MinuteMs = 60_000L;

        private static IEnumerable<int> Cells(UsageHeatmapLayout layout)
        {
            return System.Linq.Enumerable.Range(0, layout.CellCount);
        }

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

                    new TestCaseDescriptor("UsageWindow", "HeatmapRowsAreClockAligned", "Heatmap layouts start and end on row boundaries, include the current row, and stay put within it", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 10, 13, 12, 6, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageRange[] ranges = { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month };
                        long[] rowWidths = { 15 * MinuteMs, 60 * MinuteMs, 1440 * MinuteMs, 7 * 1440 * MinuteMs };
                        int[] counts = { 60, 96, 84, 70 };
                        for (int i = 0; i < ranges.Length; i++)
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(ranges[i], now, 0);
                            MuxAssert.AreEqual(rowWidths[i], layout.RowMs, "row span for " + ranges[i]);
                            MuxAssert.AreEqual(counts[i], layout.CellCount, "fixed spots for " + ranges[i]);
                            MuxAssert.IsTrue(layout.FromUnixMs <= now && now < layout.ToUnixMs, "current partial row included");
                            MuxAssert.AreEqual(0L, (layout.ToUnixMs - layout.FromUnixMs) % rowWidths[i], "whole rows for " + ranges[i]);
                            UsageHeatmapLayout sameRow = UsageHeatmapLayout.Compute(ranges[i], now + MinuteMs, 0);
                            MuxAssert.AreEqual(layout.FromUnixMs, sameRow.FromUnixMs, "axes stable within clock row");
                        }
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutShapes", "Every range has its clock-aligned heatmap shape, row starts, and column labels", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(); // a Wednesday
                        (UsageRange Range, int Rows, int Columns, long BucketMs, string RowKind, string ColumnKind, string FirstColumn, string SecondColumn)[] shapes =
                        {
                            (UsageRange.Hour, 4, 15, MinuteMs, "time", "minute", "+0m", "+1m"),
                            (UsageRange.Day, 24, 4, 15 * MinuteMs, "time", "minuteofhour", ":00", ":15"),
                            (UsageRange.Week, 7, 12, 120 * MinuteMs, "weekdate", "clock", "00:00", "02:00"),
                            (UsageRange.Month, 5, 14, 720 * MinuteMs, "date", "halfday", "Mon", "Mon"),
                            (UsageRange.Quarter, 13, 7, 1440 * MinuteMs, "date", "weekday", "Mon", "Tue"),
                            (UsageRange.Year, 4, 13, 7 * 1440 * MinuteMs, "date", "weeks", "+0w", "+1w")
                        };
                        foreach (var shape in shapes)
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(shape.Range, now, 0);
                            string name = shape.Range.ToString();
                            MuxAssert.AreEqual(shape.Rows, layout.Rows, name + " rows");
                            MuxAssert.AreEqual(shape.Columns, layout.Columns, name + " columns");
                            MuxAssert.AreEqual(shape.BucketMs, layout.BucketMs, name + " bucket");
                            MuxAssert.AreEqual(shape.RowKind, layout.RowKind, name + " row kind");
                            MuxAssert.AreEqual(shape.ColumnKind, layout.ColumnKind, name + " column kind");
                            MuxAssert.AreEqual(shape.FirstColumn, layout.ColumnLabels[0], name + " first column label");
                            MuxAssert.AreEqual(shape.SecondColumn, layout.ColumnLabels[1], name + " second column label");
                            MuxAssert.AreEqual(shape.Rows, layout.RowStartsUnixMs.Count, name + " row starts");
                            MuxAssert.AreEqual(shape.Rows, layout.RowLabels.Count, name + " row labels");
                            MuxAssert.AreEqual(layout.FromUnixMs + (long)layout.CellCount * layout.BucketMs, layout.ToUnixMs, name + " window holds every cell");
                            MuxAssert.AreEqual(layout.FromUnixMs + layout.RowMs, layout.RowStartsUnixMs.Count > 1 ? layout.RowStartsUnixMs[1] : layout.FromUnixMs + layout.RowMs, name + " rows are one row span apart");
                            int nowCell = layout.IndexOf(now);
                            MuxAssert.IsTrue(nowCell >= (layout.Rows - 1) * layout.Columns, name + ": now is in the last row");
                            MuxAssert.IsFalse(layout.IsFuture(nowCell), name + ": the cell holding now is not future");
                            MuxAssert.IsTrue(nowCell == layout.CellCount - 1 || layout.IsFuture(nowCell + 1), name + ": cells after now are future");
                            MuxAssert.Contains("Rows: ", layout.Caption, name + " caption");
                        }

                        UsageHeatmapLayout hour = UsageHeatmapLayout.Compute(UsageRange.Hour, now, 0);
                        MuxAssert.AreEqual("14:45,15:00,15:15,15:30", string.Join(",", hour.RowLabels), "hour rows are local quarter-hours");
                        UsageHeatmapLayout month = UsageHeatmapLayout.Compute(UsageRange.Month, now, 0);
                        MuxAssert.AreEqual("2026-10-12,2026-10-19,2026-10-26,2026-11-02,2026-11-09", string.Join(",", month.RowLabels), "month rows are the weeks starting Monday");
                        MuxAssert.AreEqual(4 * 14 + 2 * 2 + 1, month.IndexOf(now), "Wednesday PM of the last week");
                        UsageHeatmapLayout quarter = UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 0);
                        MuxAssert.AreEqual("2026-08-17", quarter.RowLabels[0], "quarter rows start on a Monday");
                        MuxAssert.AreEqual("2026-11-09", quarter.RowLabels[12], "quarter last row is this week");
                        MuxAssert.AreEqual("Mon,Tue,Wed,Thu,Fri,Sat,Sun", string.Join(",", quarter.ColumnLabels), "weekday columns, Monday first");
                        MuxAssert.AreEqual(12 * 7 + 2, quarter.IndexOf(now), "Wednesday of the last week");
                        UsageHeatmapLayout year = UsageHeatmapLayout.Compute(UsageRange.Year, now, 0);
                        MuxAssert.AreEqual("2025-11-17,2026-02-16,2026-05-18,2026-08-17", string.Join(",", year.RowLabels), "year rows of 13 weeks");
                        MuxAssert.Throws<ArgumentOutOfRangeException>(() => UsageHeatmapLayout.Compute((UsageRange)999, now, 0), "unknown range");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutLocalAlignment", "Heatmap rows align to local time at negative, half-hour, and 45-minute offsets, and to a fixed-offset zone", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        long hourMs = 60 * MinuteMs, dayMs = 24 * hourMs;

                        // UTC-5: the week ends at local midnight after Wednesday the 11th (05:00 UTC on the 12th).
                        UsageHeatmapLayout week = UsageHeatmapLayout.Compute(UsageRange.Week, now, -300);
                        MuxAssert.AreEqual(new DateTimeOffset(2026, 11, 12, 5, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), week.ToUnixMs, "UTC-5 week ends at local midnight");
                        MuxAssert.AreEqual("Thu 11-05", week.RowLabels[0], "UTC-5 first day row");
                        MuxAssert.AreEqual("Wed 11-11", week.RowLabels[6], "UTC-5 last day row is local today");
                        foreach (long start in week.RowStartsUnixMs) MuxAssert.AreEqual(5 * hourMs, start % dayMs, "UTC-5 rows start at local midnight");

                        // UTC+5:30: hour rows start on the local hour, which is half past in UTC.
                        UsageHeatmapLayout day = UsageHeatmapLayout.Compute(UsageRange.Day, now, 330);
                        foreach (long start in day.RowStartsUnixMs) MuxAssert.AreEqual(30 * MinuteMs, start % hourMs, "UTC+5:30 rows start on the local hour");
                        MuxAssert.AreEqual("21:00", day.RowLabels[23], "UTC+5:30 last row is the local hour holding now");
                        MuxAssert.AreEqual("22:00", day.RowLabels[0], "UTC+5:30 first row");

                        // UTC+5:45: a Monday-start quarter in local time.
                        UsageHeatmapLayout quarter = UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 345);
                        long localMonday = quarter.FromUnixMs + 345 * MinuteMs;
                        MuxAssert.AreEqual(0L, localMonday % dayMs, "UTC+5:45 rows start at local midnight");
                        MuxAssert.AreEqual(DayOfWeek.Monday, DateTimeOffset.FromUnixTimeMilliseconds(localMonday).UtcDateTime.DayOfWeek, "UTC+5:45 rows start on Monday");

                        // A fixed-offset zone (no daylight saving) matches the explicit offset.
                        TimeZoneInfo tokyo = TimeZoneInfo.CreateCustomTimeZone("mux-test-utc9", TimeSpan.FromHours(9), "UTC+9", "UTC+9");
                        UsageHeatmapLayout zoned = UsageHeatmapLayout.Compute(UsageRange.Month, now, tokyo);
                        UsageHeatmapLayout offset = UsageHeatmapLayout.Compute(UsageRange.Month, now, 540);
                        MuxAssert.AreEqual(offset.FromUnixMs, zoned.FromUnixMs, "zone and offset agree");
                        MuxAssert.AreEqual(540, zoned.UtcOffsetMinutes, "zone offset");
                        MuxAssert.AreEqual(540, UsageHeatmapLayout.OffsetMinutesOf(tokyo, now - 180L * dayMs), "no daylight saving half a year earlier");
                        MuxAssert.AreEqual(-300, UsageHeatmapLayout.FromJavaScriptOffset(300), "JavaScript sign is inverted");
                        MuxAssert.AreEqual(-840, UsageHeatmapLayout.FromJavaScriptOffset(5000), "offset clamped");
                        MuxAssert.AreEqual(840, UsageHeatmapLayout.Compute(UsageRange.Day, now, 9999).UtcOffsetMinutes, "layout offset clamped");
                        UsageHeatmapLayout sameRow = UsageHeatmapLayout.Compute(UsageRange.Day, now + MinuteMs, 330);
                        MuxAssert.AreEqual(day.FromUnixMs, sameRow.FromUnixMs, "grid stable within a row");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutMonthCalendar", "The month is a 5-week calendar of half-days from a local Monday, with only its last 30 days in the window", (CancellationToken ct) =>
                    {
                        long hourMs = 60 * MinuteMs, halfDayMs = 12 * hourMs, dayMs = 24 * hourMs, weekMs = 7 * dayMs;
                        long wednesday = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        foreach (int offset in new[] { 0, -300, 330, 540, -570 })
                        {
                            string name = "UTC" + (offset >= 0 ? "+" : "") + (offset / 60) + ":" + Math.Abs(offset % 60).ToString("00");
                            UsageHeatmapLayout month = UsageHeatmapLayout.Compute(UsageRange.Month, wednesday, offset);
                            MuxAssert.AreEqual(5, month.Rows, name + " rows");
                            MuxAssert.AreEqual(14, month.Columns, name + " columns");
                            MuxAssert.AreEqual(halfDayMs, month.BucketMs, name + " half-day cells");
                            MuxAssert.AreEqual(weekMs, month.RowMs, name + " week rows");
                            MuxAssert.AreEqual("1w", month.RowUnit, name + " row unit");
                            MuxAssert.AreEqual(UsageHeatmapLayout.ColumnKindHalfDay, month.ColumnKind, name + " column kind");
                            MuxAssert.AreEqual(UsageHeatmapLayout.RowKindDate, month.RowKind, name + " row kind");
                            MuxAssert.AreEqual(2, month.ColumnGroup, name + " two columns per weekday");
                            foreach (long start in month.RowStartsUnixMs)
                            {
                                DateTime local = DateTimeOffset.FromUnixTimeMilliseconds(start + offset * MinuteMs).UtcDateTime;
                                MuxAssert.AreEqual(DayOfWeek.Monday, local.DayOfWeek, name + " rows start on a local Monday");
                                MuxAssert.AreEqual(TimeSpan.Zero, local.TimeOfDay, name + " rows start at local midnight");
                            }

                            long localNow = wednesday + offset * MinuteMs;
                            long localStart = month.RowStartsUnixMs[4] + offset * MinuteMs;
                            MuxAssert.IsTrue(localStart <= localNow && localNow < localStart + weekMs, name + " last row is this local week");
                            int nowCell = month.IndexOf(wednesday);
                            MuxAssert.IsTrue(nowCell >= 4 * 14, name + " now is in the last row");
                            MuxAssert.IsFalse(month.IsOutsideWindow(nowCell), name + " the cell holding now is in the window");
                            MuxAssert.AreEqual(nowCell + 1, month.CellCount - Cells(month).Count(i => month.IsFuture(i)), name + " cells after now are future");

                            // The window is the last 30 days: 60 half-days ending with the one holding now.
                            int inWindow = Cells(month).Count(i => !month.IsOutsideWindow(i));
                            MuxAssert.AreEqual(60, inWindow, name + " 60 half-days in the window");
                            MuxAssert.AreEqual(month.CellStartUnixMs(nowCell) - 59 * halfDayMs, month.WindowFromUnixMs, name + " window start");
                            int first = month.IndexOf(month.WindowFromUnixMs);
                            MuxAssert.AreEqual(nowCell - 59, first, name + " first in-window cell");
                            for (int i = 0; i < first; i++) MuxAssert.IsTrue(month.IsOutsideWindow(i) && !month.IsFuture(i), name + " cells before the window are outside it");
                            MuxAssert.AreEqual(-1, month.IndexOf(month.WindowFromUnixMs - 1), name + " a call before the window has no cell");
                            UsageHeatmap heat = new UsageHeatmap(month);
                            heat.Add(month.FromUnixMs, 500, 1);
                            heat.Add(wednesday, 7, 1);
                            MuxAssert.AreEqual(0L, heat.TotalTokens[0], name + " usage before the window is not drawn");
                            MuxAssert.AreEqual(7L, heat.TotalTokens[nowCell], name + " usage now is drawn");
                        }

                        // Monday morning: the 30-day window starts before the 5-week calendar, so it holds what fits.
                        long monday = new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageHeatmapLayout early = UsageHeatmapLayout.Compute(UsageRange.Month, monday, 0);
                        MuxAssert.AreEqual(early.FromUnixMs, early.WindowFromUnixMs, "window clipped to the calendar");
                        MuxAssert.AreEqual(4 * 14, early.IndexOf(monday), "Monday AM opens the last row");
                        MuxAssert.AreEqual(57, Cells(early).Count(i => !early.IsOutsideWindow(i)), "every cell up to now is in the window");
                        MuxAssert.AreEqual("2026-10-12", early.RowLabels[0], "first week");
                        MuxAssert.AreEqual("2026-11-09", early.RowLabels[4], "this week");

                        // Sunday evening: the whole last row is in the past.
                        long sunday = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        UsageHeatmapLayout late = UsageHeatmapLayout.Compute(UsageRange.Month, sunday, 0);
                        MuxAssert.AreEqual(69, late.IndexOf(sunday), "Sunday PM is the last cell");
                        MuxAssert.AreEqual(10, late.IndexOf(late.WindowFromUnixMs), "the first 10 half-days are before the window");

                        // Labels: each weekday names its AM and PM column and is labeled once, over the AM column.
                        UsageHeatmapLayout labels = UsageHeatmapLayout.Compute(UsageRange.Month, wednesday, 0);
                        MuxAssert.AreEqual("Mon,Mon,Tue,Tue,Wed,Wed,Thu,Thu,Fri,Fri,Sat,Sat,Sun,Sun", string.Join(",", labels.ColumnLabels), "half-day column labels");
                        MuxAssert.AreEqual("0,2,4,6,8,10,12", string.Join(",", labels.LabeledColumns(14)), "one label per day");
                        MuxAssert.AreEqual("0,4,8,12", string.Join(",", labels.LabeledColumns(4)), "thinned by whole days");
                        MuxAssert.AreEqual("Rows: week starting · Columns: day (AM, PM)", labels.Caption, "caption");
                        MuxAssert.AreEqual(1, UsageHeatmapLayout.Compute(UsageRange.Quarter, wednesday, 0).ColumnGroup, "other ranges label every column");
                        UsageHeatmapLayout quarter = UsageHeatmapLayout.Compute(UsageRange.Quarter, wednesday, 0);
                        MuxAssert.AreEqual(quarter.FromUnixMs, quarter.WindowFromUnixMs, "other ranges use the whole grid");
                        MuxAssert.AreEqual(labels.RowStartsUnixMs[4], quarter.RowStartsUnixMs[12], "month and quarter share the Monday week start");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutAllTime", "All time sizes its rows from the oldest event and picks the shape from its bucket width", (CancellationToken ct) =>
                    {
                        long now = new DateTimeOffset(2026, 11, 11, 15, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
                        long hourMs = 60 * MinuteMs, dayMs = 24 * hourMs;

                        long longAgo = now - 200 * dayMs;
                        UsageHeatmapLayout weeks = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, longAgo);
                        MuxAssert.AreEqual("all", weeks.Range, "range key");
                        MuxAssert.AreEqual(7 * dayMs, weeks.BucketMs, "weekly buckets for 200 days");
                        MuxAssert.AreEqual(13, weeks.Columns, "13 weekly columns");
                        MuxAssert.AreEqual(3, weeks.Rows, "rows sized from the count");
                        MuxAssert.IsTrue(weeks.FromUnixMs <= longAgo && longAgo - weeks.FromUnixMs < weeks.RowMs, "first row holds the oldest event");

                        long recent = now - 89 * hourMs;
                        UsageHeatmapLayout days = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, recent);
                        MuxAssert.AreEqual(hourMs, days.BucketMs, "hourly buckets");
                        MuxAssert.AreEqual(24, days.Columns, "hour-of-day columns");
                        MuxAssert.AreEqual("clock", days.ColumnKind, "clock columns");
                        MuxAssert.AreEqual(5, days.Rows, "five day rows from the 7th to the 11th");
                        MuxAssert.AreEqual("Sat 11-07", days.RowLabels[0], "first day row");

                        UsageHeatmapLayout halfHours = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - 40 * hourMs);
                        MuxAssert.AreEqual(30 * MinuteMs, halfHours.BucketMs, "half-hour buckets");
                        MuxAssert.AreEqual("6h", halfHours.RowUnit, "six-hour rows");
                        MuxAssert.AreEqual("datetime", halfHours.RowKind, "rows spanning days show the date");
                        MuxAssert.AreEqual("+1h", halfHours.ColumnLabels[2], "hour offsets");

                        UsageHeatmapLayout none = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, 0);
                        UsageHeatmapLayout dayLayout = UsageHeatmapLayout.Compute(UsageRange.Day, now, 0);
                        MuxAssert.AreEqual(dayLayout.FromUnixMs, none.FromUnixMs, "no events falls back to the day layout");
                        MuxAssert.AreEqual(dayLayout.CellCount, none.CellCount, "fallback cell count");

                        MuxAssert.AreEqual("0,3,6,9,12,15,18,23", string.Join(",", UsageHeatmapLayout.LabeledIndices(24, 8, true)), "dense rows thin out but keep the last");
                        MuxAssert.AreEqual("0,1,2,3,4,5,6", string.Join(",", UsageHeatmapLayout.LabeledIndices(7, 10, true)), "sparse rows all labeled");
                        MuxAssert.AreEqual("0,4,8", string.Join(",", UsageHeatmapLayout.LabeledIndices(12, 3, false)), "columns thin without forcing the last");
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
