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

        private const long DayMs = 24L * 60L * MinuteMs;

        private static IEnumerable<int> Cells(UsageHeatmapLayout layout)
        {
            return System.Linq.Enumerable.Range(0, layout.CellCount);
        }

        private static long Utc(int year, int month, int day, int hour, int minute)
        {
            return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        }

        // Invariants every layout keeps: cells tile [From, To) once each in bucket steps, chronological order matches
        // the cell starts, IndexOf round-trips every cell inside the window, the out-of-window flags match the window,
        // and every axis has one label and one instant per row and column.
        private static void CheckLayout(UsageHeatmapLayout layout, string name)
        {
            MuxAssert.AreEqual(layout.CellCount, layout.CellStartsUnixMs.Count, name + " one start per cell");
            List<int> order = layout.CellsInTimeOrder().ToList();
            MuxAssert.AreEqual(layout.CellCount, order.Distinct().Count(), name + " time order visits each cell once");
            for (int k = 0; k < order.Count; k++)
            {
                MuxAssert.AreEqual(layout.FromUnixMs + (k * layout.BucketMs), layout.CellStartUnixMs(order[k]), name + " cells tile the grid in time order");
            }

            MuxAssert.AreEqual(layout.ToUnixMs, layout.FromUnixMs + (layout.CellCount * layout.BucketMs), name + " grid end");
            for (int i = 0; i < layout.CellCount; i++)
            {
                long start = layout.CellStartUnixMs(i);
                bool outside = start < layout.WindowFromUnixMs || start > layout.NowUnixMs;
                MuxAssert.AreEqual(outside, layout.IsOutsideWindow(i), name + " outside flag of cell " + i);
                MuxAssert.AreEqual(outside, layout.OutsideWindow[i], name + " serialized outside flag of cell " + i);
                if (start >= layout.WindowFromUnixMs) MuxAssert.AreEqual(i, layout.IndexOf(start + layout.BucketMs - 1), name + " IndexOf round-trips cell " + i);
            }

            MuxAssert.AreEqual(layout.Columns, layout.ColumnLabels.Count, name + " column labels");
            MuxAssert.AreEqual(layout.Columns, layout.ColumnTimesUnixMs.Count, name + " column instants");
            MuxAssert.AreEqual(layout.Rows, layout.RowLabels.Count, name + " row labels");
            MuxAssert.AreEqual(layout.Rows, layout.RowTimesUnixMs.Count, name + " row instants");
            MuxAssert.AreEqual(0, layout.ColumnLabelAnchors[0], name + " the first column is an anchor");
            MuxAssert.IsTrue(layout.IndexOf(layout.NowUnixMs) >= 0, name + " now is on the grid");
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

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutShapes", "Every range has its heatmap shape: time runs left to right along the x-axis, steps down the y-axis, month a calendar", (CancellationToken ct) =>
                    {
                        long now = Utc(2026, 11, 11, 15, 30); // a Wednesday
                        (UsageRange Range, int Columns, int Rows, long BucketMs, string ColumnKind, string RowKind, string ColumnUnit, string FirstColumn, string LastColumn, string FirstRow, string LastRow)[] shapes =
                        {
                            (UsageRange.Hour, 12, 5, MinuteMs, "minuteofhour", "minute", "5m", ":35", ":30", "+0m", "+4m"),
                            (UsageRange.Day, 24, 4, 15 * MinuteMs, "hour", "minuteofhour", "1h", "16", "15", ":00", ":45"),
                            (UsageRange.Week, 7, 12, 120 * MinuteMs, "weekdate", "clock", "1d", "Thu 11/5", "Wed 11/11", "00:00", "22:00"),
                            (UsageRange.Month, 14, 5, 720 * MinuteMs, "halfday", "week", "halfday", "Mon", "Sun", "2026-10-12", "2026-11-09"),
                            (UsageRange.Quarter, 13, 7, DayMs, "date", "weekday", "1w", "8/17", "11/9", "Mon", "Sun"),
                            (UsageRange.Year, 53, 7, DayMs, "month", "weekday", "month", "Nov", "Nov", "Mon", "Sun")
                        };
                        foreach (var shape in shapes)
                        {
                            UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(shape.Range, now, 0);
                            string name = shape.Range.ToString();
                            MuxAssert.AreEqual(shape.Columns, layout.Columns, name + " columns (x)");
                            MuxAssert.AreEqual(shape.Rows, layout.Rows, name + " rows (y)");
                            MuxAssert.AreEqual(shape.BucketMs, layout.BucketMs, name + " bucket");
                            MuxAssert.AreEqual(shape.ColumnKind, layout.ColumnKind, name + " column kind");
                            MuxAssert.AreEqual(shape.RowKind, layout.RowKind, name + " row kind");
                            MuxAssert.AreEqual(shape.ColumnUnit, layout.ColumnUnit, name + " column unit");
                            MuxAssert.AreEqual(shape.FirstColumn, layout.ColumnLabels[0], name + " first column label");
                            MuxAssert.AreEqual(shape.LastColumn, layout.ColumnLabels[layout.Columns - 1], name + " last column label");
                            MuxAssert.AreEqual(shape.FirstRow, layout.RowLabels[0], name + " first row label");
                            MuxAssert.AreEqual(shape.LastRow, layout.RowLabels[layout.Rows - 1], name + " last row label");
                            MuxAssert.IsTrue(layout.Columns > layout.Rows || shape.Range == UsageRange.Week, name + " is wider than tall (week is 7 days of 12 two-hour steps)");
                            CheckLayout(layout, name);
                        }

                        MuxAssert.AreEqual("Columns: hour of day · Rows: minute of the hour", UsageHeatmapLayout.Compute(UsageRange.Day, now, 0).Caption, "day caption");
                        MuxAssert.AreEqual("Columns: 5 minutes starting · Rows: minutes after the column start", UsageHeatmapLayout.Compute(UsageRange.Hour, now, 0).Caption, "hour caption");
                        MuxAssert.AreEqual("Columns: day · Rows: time of day", UsageHeatmapLayout.Compute(UsageRange.Week, now, 0).Caption, "week caption");
                        MuxAssert.AreEqual("Columns: day of the week (AM, PM) · Rows: week starting", UsageHeatmapLayout.Compute(UsageRange.Month, now, 0).Caption, "month caption");
                        MuxAssert.AreEqual("Columns: week starting · Rows: day of the week", UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 0).Caption, "quarter caption");
                        MuxAssert.AreEqual("Columns: week (by month) · Rows: day of the week", UsageHeatmapLayout.Compute(UsageRange.Year, now, 0).Caption, "year caption");

                        // Display order is row-major; time runs down a column, then to the next column.
                        UsageHeatmapLayout day = UsageHeatmapLayout.Compute(UsageRange.Day, now, 0);
                        MuxAssert.AreEqual(day.FromUnixMs + 15 * MinuteMs, day.CellStartUnixMs(day.IndexAt(1, 0)), "row 1 of the first hour is its :15");
                        MuxAssert.AreEqual(day.FromUnixMs + 60 * MinuteMs, day.CellStartUnixMs(day.IndexAt(0, 1)), "column 1 is the next hour");
                        MuxAssert.AreEqual(day.IndexAt(2, 23), day.IndexOf(Utc(2026, 11, 11, 15, 30)), "now lands in the last column's :30 row");
                        MuxAssert.AreEqual(-1, day.IndexOf(day.FromUnixMs - 1), "before the grid");
                        MuxAssert.AreEqual(-1, day.IndexOf(day.ToUnixMs), "after the grid");
                        UsageHeatmapLayout quarter = UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 0);
                        MuxAssert.AreEqual(quarter.IndexAt(2, 12), quarter.IndexOf(now), "quarter: Wednesday of the last week");

                        // Hour: twelve five-minute columns of 1-minute rows, ending with the one holding now.
                        UsageHeatmapLayout hour = UsageHeatmapLayout.Compute(UsageRange.Hour, now, 0);
                        MuxAssert.AreEqual(Utc(2026, 11, 11, 15, 35), hour.ToUnixMs, "hour ends after the 5-minute period holding now");
                        MuxAssert.AreEqual(hour.IndexAt(0, 11), hour.IndexOf(now), "now is the first minute of the last column");
                        MuxAssert.AreEqual(4, Cells(hour).Count(i => hour.IsOutsideWindow(i)), "the rest of the last column is in the future");

                        // Out-of-window flags and cell starts are exposed per cell, in display order.
                        MuxAssert.AreEqual(hour.CellCount, hour.CellStartsUnixMs.Count, "cell starts");
                        MuxAssert.AreEqual(hour.CellCount, hour.OutsideWindow.Count, "outside flags");
                        MuxAssert.Throws<ArgumentOutOfRangeException>(() => UsageHeatmapLayout.Compute((UsageRange)999, now, 0), "unknown range");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutLocalAlignment", "Heatmap columns align to local time at negative, half-hour, and 45-minute offsets, and to a fixed-offset zone", (CancellationToken ct) =>
                    {
                        long now = Utc(2026, 11, 11, 15, 30);
                        long hourMs = 60 * MinuteMs;

                        // UTC-5: the week ends at local midnight after Wednesday the 11th (05:00 UTC on the 12th).
                        UsageHeatmapLayout week = UsageHeatmapLayout.Compute(UsageRange.Week, now, -300);
                        MuxAssert.AreEqual(Utc(2026, 11, 12, 5, 0), week.ToUnixMs, "UTC-5 week ends at local midnight");
                        MuxAssert.AreEqual("Thu 11/5", week.ColumnLabels[0], "UTC-5 first day column");
                        MuxAssert.AreEqual("Wed 11/11", week.ColumnLabels[6], "UTC-5 last day column is local today");
                        foreach (long start in week.ColumnTimesUnixMs) MuxAssert.AreEqual(5 * hourMs, start % DayMs, "UTC-5 columns start at local midnight");
                        MuxAssert.AreEqual("10:00", week.RowLabels[5], "rows are local times of day");
                        CheckLayout(week, "UTC-5 week");

                        // UTC+5:30: hour columns start on the local hour, which is half past in UTC.
                        UsageHeatmapLayout day = UsageHeatmapLayout.Compute(UsageRange.Day, now, 330);
                        foreach (long start in day.ColumnTimesUnixMs) MuxAssert.AreEqual(30 * MinuteMs, start % hourMs, "UTC+5:30 columns start on the local hour");
                        MuxAssert.AreEqual("21", day.ColumnLabels[23], "UTC+5:30 last column is the local hour holding now");
                        MuxAssert.AreEqual("22", day.ColumnLabels[0], "UTC+5:30 first column");
                        CheckLayout(day, "UTC+5:30 day");

                        // UTC+5:45: a Monday-start quarter in local time, weekday rows Monday first.
                        UsageHeatmapLayout quarter = UsageHeatmapLayout.Compute(UsageRange.Quarter, now, 345);
                        long localMonday = quarter.FromUnixMs + 345 * MinuteMs;
                        MuxAssert.AreEqual(0L, localMonday % DayMs, "UTC+5:45 columns start at local midnight");
                        MuxAssert.AreEqual(DayOfWeek.Monday, DateTimeOffset.FromUnixTimeMilliseconds(localMonday).UtcDateTime.DayOfWeek, "UTC+5:45 columns start on Monday");
                        MuxAssert.AreEqual("Mo", quarter.FormatRowLabel(0, new System.Globalization.CultureInfo("de-DE")), "weekday rows localize");
                        MuxAssert.AreEqual("09.11", quarter.FormatColumnLabel(12, new System.Globalization.CultureInfo("de-DE")), "week start dates localize the month-day order");
                        CheckLayout(quarter, "UTC+5:45 quarter");

                        // A fixed-offset zone (no daylight saving) matches the explicit offset.
                        TimeZoneInfo tokyo = TimeZoneInfo.CreateCustomTimeZone("mux-test-utc9", TimeSpan.FromHours(9), "UTC+9", "UTC+9");
                        UsageHeatmapLayout zoned = UsageHeatmapLayout.Compute(UsageRange.Month, now, tokyo);
                        UsageHeatmapLayout offset = UsageHeatmapLayout.Compute(UsageRange.Month, now, 540);
                        MuxAssert.AreEqual(offset.FromUnixMs, zoned.FromUnixMs, "zone and offset agree");
                        MuxAssert.AreEqual(540, zoned.UtcOffsetMinutes, "zone offset");
                        MuxAssert.AreEqual(540, UsageHeatmapLayout.OffsetMinutesOf(tokyo, now - 180L * DayMs), "no daylight saving half a year earlier");
                        MuxAssert.AreEqual(-300, UsageHeatmapLayout.FromJavaScriptOffset(300), "JavaScript sign is inverted");
                        MuxAssert.AreEqual(-840, UsageHeatmapLayout.FromJavaScriptOffset(5000), "offset clamped");
                        MuxAssert.AreEqual(840, UsageHeatmapLayout.Compute(UsageRange.Day, now, 9999).UtcOffsetMinutes, "layout offset clamped");
                        UsageHeatmapLayout sameColumn = UsageHeatmapLayout.Compute(UsageRange.Day, now + MinuteMs, 330);
                        MuxAssert.AreEqual(day.FromUnixMs, sameColumn.FromUnixMs, "grid stable within a period");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutMonthCalendar", "The month calendar always holds its 30-day window: 5 weeks, or 6 on a Monday or a Tuesday before noon, at any offset", (CancellationToken ct) =>
                    {
                        int[] offsets = { 0, -300, 330, -570 };
                        // (local day of November 2026, hour, minute, expected rows)
                        (int Day, int Hour, int Minute, int Rows, string Name)[] cases =
                        {
                            (9, 0, 0, 6, "Monday midnight"),
                            (9, 8, 0, 6, "Monday morning"),
                            (9, 18, 0, 6, "Monday evening"),
                            (10, 11, 59, 6, "Tuesday before noon"),
                            (10, 12, 0, 5, "Tuesday noon"),
                            (11, 15, 30, 5, "Wednesday"),
                            (15, 21, 0, 5, "Sunday evening")
                        };
                        foreach (int offset in offsets)
                        {
                            foreach (var c in cases)
                            {
                                long now = Utc(2026, 11, c.Day, c.Hour, c.Minute) - offset * MinuteMs;
                                string name = c.Name + " at " + offset;
                                UsageHeatmapLayout month = UsageHeatmapLayout.Compute(UsageRange.Month, now, offset);
                                MuxAssert.AreEqual(c.Rows, month.Rows, name + " rows");
                                MuxAssert.AreEqual(14, month.Columns, name + " columns");
                                MuxAssert.AreEqual(2, month.ColumnGroup, name + " half-days share a label");
                                MuxAssert.AreEqual(UsageHeatmapLayout.RowKindWeek, month.RowKind, name + " row kind");
                                DateTime localFrom = DateTimeOffset.FromUnixTimeMilliseconds(month.FromUnixMs + offset * MinuteMs).UtcDateTime;
                                MuxAssert.AreEqual(DayOfWeek.Monday, localFrom.DayOfWeek, name + " starts on a local Monday");
                                MuxAssert.AreEqual(TimeSpan.Zero, localFrom.TimeOfDay, name + " starts at local midnight");
                                MuxAssert.IsTrue(month.WindowFromUnixMs >= month.FromUnixMs, name + " the window fits the grid");
                                int inWindow = Cells(month).Count(i => !month.IsOutsideWindow(i));
                                MuxAssert.AreEqual(60, inWindow, name + " the whole 30-day window is drawn");
                                MuxAssert.IsTrue(month.WindowFromUnixMs - month.FromUnixMs < 7 * DayMs, name + " no wasted first week");
                                MuxAssert.IsTrue(month.ToUnixMs > now, name + " the last row holds now");
                                MuxAssert.AreEqual(month.IndexAt(month.Rows - 1, 0), month.IndexOf(month.FromUnixMs + (month.Rows - 1) * 7 * DayMs), name + " the last row is the current week");
                                CheckLayout(month, name);
                            }
                        }

                        long wednesday = Utc(2026, 11, 11, 15, 30);
                        UsageHeatmapLayout labels = UsageHeatmapLayout.Compute(UsageRange.Month, wednesday, 0);
                        MuxAssert.AreEqual("0,2,4,6,8,10,12", string.Join(",", labels.ColumnLabelAnchors), "each weekday labeled once, over its AM column");
                        MuxAssert.AreEqual("0,2,4,6,8,10,12", string.Join(",", labels.PlaceColumnLabels(1)), "every weekday, each at its AM column");
                        MuxAssert.AreEqual("0,2,4,6,8,10,12", string.Join(",", labels.PlaceColumnLabels(2)), "Sun fits in Sun AM and PM");
                        MuxAssert.AreEqual("Sun", labels.ColumnLabels[labels.PlaceColumnLabels(2)[6]], "the last label is Sunday, at its AM column");
                        MuxAssert.AreEqual("0,4,8", string.Join(",", labels.PlaceColumnLabels(3)), "every other weekday when they crowd; Sun at 12 would run past the grid, so it is dropped, not moved");
                        MuxAssert.AreEqual("0", string.Join(",", labels.PlaceColumnLabels(7)), "only Mon when one label needs half the grid");
                        MuxAssert.AreEqual("Mon", labels.ColumnLabels[1], "the PM column repeats its weekday");
                        MuxAssert.AreEqual("0,1", string.Join(",", Cells(labels).Where(i => labels.IsOutsideWindow(i) && !labels.IsFuture(i))), "Wednesday PM: Monday 10-12 is before the window");
                        UsageHeatmap heat = new UsageHeatmap(labels);
                        heat.Add(labels.FromUnixMs + MinuteMs, 5, 1);
                        heat.Add(labels.WindowFromUnixMs, 7, 1);
                        MuxAssert.AreEqual(0L, heat.TotalTokens[0], "usage before the window is not counted");
                        MuxAssert.AreEqual(7L, heat.TotalTokens[2], "the window's first half-day");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutYear", "The year is daily cells for the last 364 days in 52 or 53 Monday-start week columns, labeled where months start", (CancellationToken ct) =>
                    {
                        long wednesday = Utc(2026, 11, 11, 15, 30);
                        UsageHeatmapLayout year = UsageHeatmapLayout.Compute(UsageRange.Year, wednesday, 0);
                        MuxAssert.AreEqual(53, year.Columns, "a Wednesday needs 53 weeks");
                        MuxAssert.AreEqual(7, year.Rows, "weekday rows");
                        MuxAssert.AreEqual(DayMs, year.BucketMs, "daily cells, not the year chart's weekly buckets");
                        MuxAssert.AreEqual(Utc(2025, 11, 10, 0, 0), year.FromUnixMs, "the first column is the Monday of the week holding the window start");
                        MuxAssert.AreEqual(Utc(2025, 11, 13, 0, 0), year.WindowFromUnixMs, "the window is the last 364 days, from local midnight");
                        MuxAssert.AreEqual(364, Cells(year).Count(i => !year.IsOutsideWindow(i)), "364 days in the window");
                        MuxAssert.AreEqual("0,53,106", string.Join(",", Cells(year).Where(i => year.IsOutsideWindow(i) && !year.IsFuture(i))), "Mon-Wed of the first week are before the window");
                        MuxAssert.AreEqual(4, Cells(year).Count(i => year.IsFuture(i)), "Thu-Sun of this week are in the future");
                        MuxAssert.AreEqual(year.IndexAt(2, 52), year.IndexOf(wednesday), "today is Wednesday of the last column");
                        MuxAssert.AreEqual(14, year.ColumnLabelAnchors.Count, "the first and last column plus 12 month starts");
                        MuxAssert.AreEqual("Nov,Dec,Jan,Feb,Mar,Apr,May,Jun,Jul,Aug,Sep,Oct,Nov,Nov", string.Join(",", year.ColumnLabelAnchors.Select(c => year.ColumnLabels[c])), "month labels where months start");
                        MuxAssert.AreEqual(3, year.ColumnLabelAnchors[1], "December 1st 2025 (a Monday) starts column 3");
                        List<int> picked = year.PlaceColumnLabels(3);
                        MuxAssert.AreEqual(0, picked[0], "first labeled");
                        for (int i = 1; i < picked.Count; i++) MuxAssert.IsTrue(picked[i] - picked[i - 1] >= 3, "month labels never collide");
                        MuxAssert.AreEqual(50, picked[picked.Count - 1], "November starts column 50, which fits; the last column (52) would run past the grid, so it is dropped");
                        MuxAssert.IsTrue(picked[picked.Count - 1] + 3 <= year.Columns, "the last label stays inside the grid");
                        MuxAssert.AreEqual(52, year.PlaceColumnLabels(1)[year.PlaceColumnLabels(1).Count - 1], "the last column is labeled when it fits left-justified");
                        CheckLayout(year, "year");

                        long sunday = Utc(2026, 11, 15, 21, 0);
                        UsageHeatmapLayout full = UsageHeatmapLayout.Compute(UsageRange.Year, sunday, 0);
                        MuxAssert.AreEqual(52, full.Columns, "a Sunday's 364 days are exactly 52 weeks");
                        MuxAssert.AreEqual(0, Cells(full).Count(i => full.IsOutsideWindow(i)), "every cell in the window");
                        CheckLayout(full, "year on a Sunday");

                        UsageHeatmapLayout tokyo = UsageHeatmapLayout.Compute(UsageRange.Year, wednesday, 540);
                        MuxAssert.AreEqual(364, Cells(tokyo).Count(i => !tokyo.IsOutsideWindow(i)), "UTC+9 keeps 364 days");
                        CheckLayout(tokyo, "UTC+9 year");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLayoutAllTime", "All time adds columns back to the oldest event, picks the rows from its bucket width, and stays wider than tall", (CancellationToken ct) =>
                    {
                        long now = Utc(2026, 11, 11, 15, 30);
                        long hourMs = 60 * MinuteMs;

                        UsageHeatmapLayout weeks = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - 400 * DayMs);
                        MuxAssert.AreEqual(7 * DayMs, weeks.BucketMs, "weekly buckets for a long history");
                        MuxAssert.AreEqual(4, weeks.Rows, "four weeks per column");
                        MuxAssert.AreEqual(UsageHeatmapLayout.RowKindWeeks, weeks.RowKind, "rows are weeks after the column start");
                        MuxAssert.AreEqual(UsageHeatmapLayout.ColumnKindFullDate, weeks.ColumnKind, "columns carry the year");
                        MuxAssert.IsTrue(weeks.FromUnixMs <= now - 400 * DayMs, "covers the oldest event");
                        MuxAssert.AreEqual("+3w", weeks.RowLabels[3], "week offsets");

                        UsageHeatmapLayout days = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - 20 * DayMs);
                        MuxAssert.AreEqual(6 * hourMs, days.BucketMs, "6-hour buckets");
                        MuxAssert.AreEqual(UsageHeatmapLayout.ColumnKindWeekDate, days.ColumnKind, "day columns");
                        MuxAssert.AreEqual("18:00", days.RowLabels[3], "clock rows");

                        UsageHeatmapLayout halfHours = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - 40 * hourMs);
                        MuxAssert.AreEqual(30 * MinuteMs, halfHours.BucketMs, "half-hour buckets");
                        MuxAssert.AreEqual("+1h30m", halfHours.RowLabels[3], "hour offsets");
                        MuxAssert.AreEqual(UsageHeatmapLayout.ColumnKindDateTime, halfHours.ColumnKind, "a span over a day shows the date");

                        UsageHeatmapLayout minutes = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - 50 * MinuteMs);
                        MuxAssert.AreEqual(MinuteMs, minutes.BucketMs, "minute buckets");
                        MuxAssert.AreEqual(UsageHeatmapLayout.ColumnKindTime, minutes.ColumnKind, "a span within a day shows times");
                        MuxAssert.AreEqual(5, minutes.Rows, "five minutes per column");

                        UsageHeatmapLayout daily = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - 80 * DayMs);
                        MuxAssert.AreEqual(DayMs, daily.BucketMs, "daily buckets");
                        MuxAssert.AreEqual(UsageHeatmapLayout.RowKindWeekday, daily.RowKind, "weekday rows");
                        MuxAssert.AreEqual(UsageHeatmapLayout.ColumnKindDate, daily.ColumnKind, "week start columns");

                        foreach (long span in new[] { hourMs, 5 * hourMs, DayMs, 3 * DayMs, 10 * DayMs, 40 * DayMs, 80 * DayMs, 200 * DayMs, 1100 * DayMs })
                        {
                            UsageHeatmapLayout all = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, now - span);
                            string name = "all over " + (span / hourMs) + "h";
                            MuxAssert.IsTrue(all.Rows <= 7, name + " has at most 7 rows");
                            MuxAssert.IsTrue(all.Columns >= all.Rows, name + " is wider than tall");
                            MuxAssert.IsTrue(all.FromUnixMs <= now - span && all.ToUnixMs > now, name + " covers the history and now");
                            MuxAssert.AreEqual("all", all.Range, name + " range key");
                            CheckLayout(all, name);
                        }

                        UsageHeatmapLayout none = UsageHeatmapLayout.Compute(UsageRange.All, now, 0, 0);
                        UsageHeatmapLayout dayLayout = UsageHeatmapLayout.Compute(UsageRange.Day, now, 0);
                        MuxAssert.AreEqual(dayLayout.FromUnixMs, none.FromUnixMs, "no events falls back to the day grid");
                        MuxAssert.AreEqual(dayLayout.CellCount, none.CellCount, "same shape as day");
                        MuxAssert.AreEqual("all", none.Range, "but keeps its range key");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("UsageWindow", "HeatmapLabelThinning", "x-axis labels are left-justified at their columns, thinned so none collide or run past the grid, the first always and the last whenever it fits", (CancellationToken ct) =>
                    {
                        long now = Utc(2026, 11, 11, 15, 30);
                        UsageHeatmapLayout day = UsageHeatmapLayout.Compute(UsageRange.Day, now, 0);
                        MuxAssert.AreEqual(24, day.PlaceColumnLabels(1).Count, "room for every hour, the last included");
                        MuxAssert.AreEqual("0,3,6,9,12,15,18,21", string.Join(",", day.PlaceColumnLabels(3)), "every 3rd hour; the last would run past the grid, so it is dropped");
                        MuxAssert.AreEqual("0", string.Join(",", day.PlaceColumnLabels(30)), "too narrow for any label: only the first");
                        foreach (UsageRange range in new[] { UsageRange.Hour, UsageRange.Day, UsageRange.Week, UsageRange.Month, UsageRange.Quarter, UsageRange.Year })
                        {
                            foreach (long at in new[] { now, Utc(2026, 11, 9, 8, 0), Utc(2026, 11, 15, 21, 0), Utc(2026, 3, 1, 12, 0) })
                            {
                                UsageHeatmapLayout layout = UsageHeatmapLayout.Compute(range, at, 0);
                                int end = layout.ColumnLabelAnchors[layout.ColumnLabelAnchors.Count - 1];
                                for (int gap = 1; gap <= 12; gap++)
                                {
                                    List<int> placed = layout.PlaceColumnLabels(gap);
                                    string name = range + " (" + layout.Columns + "x" + layout.Rows + ") gap " + gap;
                                    MuxAssert.AreEqual(0, placed[0], name + " first labeled");
                                    for (int i = 0; i < placed.Count; i++)
                                    {
                                        // Each label takes [column, column + gap) from its column's left edge.
                                        int c = placed[i];
                                        MuxAssert.IsTrue(layout.ColumnLabelAnchors.Contains(c), name + " " + c + " is an anchor");
                                        if (layout.ColumnGroup > 1) MuxAssert.AreEqual(0, c % layout.ColumnGroup, name + " " + c + " a weekday starts at its AM column");
                                        if (i == 0) continue;
                                        MuxAssert.IsTrue(c + gap <= layout.Columns, name + " " + c + " never runs past the grid");
                                        MuxAssert.IsTrue(c - placed[i - 1] >= gap, name + " " + c + " does not collide with the label before it");
                                    }

                                    bool lastFits = end != 0 && end + gap <= layout.Columns && end >= gap;
                                    MuxAssert.AreEqual(lastFits, placed[placed.Count - 1] == end, name + " last labeled exactly when it fits left-justified beside the first");
                                }
                            }
                        }

                        MuxAssert.AreEqual("0,4,8", string.Join(",", UsageHeatmapLayout.PlaceLabels(new[] { 0, 2, 4, 6, 8, 10, 12 }, 14, 3, true)), "half-day groups");
                        MuxAssert.AreEqual("0,2,4,6,8,10", string.Join(",", UsageHeatmapLayout.PlaceLabels(Enumerable.Range(0, 13).ToList(), 13, 2, true)), "the same as VS Code and the web dashboard");
                        MuxAssert.AreEqual("0,4,9", string.Join(",", UsageHeatmapLayout.PlaceLabels(new[] { 0, 4, 8, 9 }, 12, 3, false)), "a last label that fits replaces the one it would collide with");
                        MuxAssert.AreEqual(0, UsageHeatmapLayout.PlaceLabels(Array.Empty<int>(), 0, 1, true).Count, "no anchors");
                        MuxAssert.AreEqual("0,3,6,9,12,15,18,23", string.Join(",", UsageHeatmapLayout.LabeledIndices(24, 8, true)), "dense rows thin out but keep the last");
                        MuxAssert.AreEqual("0,1,2,3,4,5,6", string.Join(",", UsageHeatmapLayout.LabeledIndices(7, 10, true)), "sparse rows all labeled");
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
