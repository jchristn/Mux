namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The clock-aligned grid of the token heatmap, shared by every surface (the web dashboard and VS Code through
    /// <c>GET /v1.0/api/usage/heatmap</c>, the desktop app and the terminal in process). The axes are explicit:
    /// <see cref="Columns"/> run along the x-axis (left to right) and <see cref="Rows"/> down the y-axis, and cell
    /// index i is row-major in display order (row x <see cref="Columns"/> + column). Time runs left to right: each
    /// column is one local-time period (labeled along the x-axis) and each row one step within it (labeled on the
    /// left), like a contribution graph:
    /// hour = 12 five-minute columns x 5 one-minute rows; day = 24 hour columns x 4 quarter-hour rows;
    /// week = 7 day columns x 12 two-hour rows; quarter = 13 Monday-start week columns x 7 weekday rows;
    /// year = 52 or 53 Monday-start week columns x 7 weekday rows of daily cells, covering the last 364 days;
    /// all = columns and rows chosen from its bucket width, with as many columns as the history needs.
    /// The month is the exception, a calendar: 14 half-day columns (Mon AM, Mon PM, ... Sun PM) x 5 or 6 Monday-start
    /// week rows, with a 6th row whenever its last 30 days do not fit in 5 weeks (<see cref="WindowFromUnixMs"/>).
    /// Every cell's start (<see cref="CellStartsUnixMs"/>), every row's and column's label, and whether a cell is
    /// outside the window (<see cref="OutsideWindow"/>: after now, or before the window) come from here, so surfaces
    /// draw rows x columns generically and never recompute a cell's time. Periods align to the viewer's local time at
    /// a fixed UTC offset (the offset at "now").
    /// </summary>
    public sealed class UsageHeatmapLayout
    {
        #region Public-Members

        /// <summary>Column (x) labels are the minute of the hour a five-minute period starts at (":05").</summary>
        public const string ColumnKindMinuteOfHour = "minuteofhour";

        /// <summary>Column (x) labels are the local hour ("14").</summary>
        public const string ColumnKindHour = "hour";

        /// <summary>Column (x) labels are a local time ("14:30").</summary>
        public const string ColumnKindTime = "time";

        /// <summary>Column (x) labels are a local date and time ("11/5 14:00").</summary>
        public const string ColumnKindDateTime = "datetime";

        /// <summary>Column (x) labels are a weekday and date ("Thu 11/5").</summary>
        public const string ColumnKindWeekDate = "weekdate";

        /// <summary>Columns (x) are half-days, Monday AM first (two per weekday, each pair labeled once with the weekday).</summary>
        public const string ColumnKindHalfDay = "halfday";

        /// <summary>Column (x) labels are a week's start date ("11/2").</summary>
        public const string ColumnKindDate = "date";

        /// <summary>Column (x) labels are month names, on the week columns where a month starts ("Nov").</summary>
        public const string ColumnKindMonth = "month";

        /// <summary>Column (x) labels are a full date ("2026-11-02").</summary>
        public const string ColumnKindFullDate = "fulldate";

        /// <summary>Row (y) labels are minutes after the column start ("+2m").</summary>
        public const string RowKindMinute = "minute";

        /// <summary>Row (y) labels are minutes of the hour (":15").</summary>
        public const string RowKindMinuteOfHour = "minuteofhour";

        /// <summary>Row (y) labels are hours after the column start ("+2h", "+1h30m").</summary>
        public const string RowKindHours = "hours";

        /// <summary>Row (y) labels are times of day ("02:00").</summary>
        public const string RowKindClock = "clock";

        /// <summary>Row (y) labels are weekdays, Monday first.</summary>
        public const string RowKindWeekday = "weekday";

        /// <summary>Row (y) labels are a week's start date (the month calendar).</summary>
        public const string RowKindWeek = "week";

        /// <summary>Row (y) labels are weeks after the column start ("+2w").</summary>
        public const string RowKindWeeks = "weeks";

        /// <summary>The range key ("hour", "day", "week", "month", "quarter", "year", "all").</summary>
        public string Range
        {
            get => _Range;
        }

        /// <summary>The reference time the layout was computed for, Unix epoch milliseconds.</summary>
        public long NowUnixMs
        {
            get => _NowUnixMs;
        }

        /// <summary>The viewer's local offset from UTC in minutes (east positive; UTC-5 is -300).</summary>
        public int UtcOffsetMinutes
        {
            get => _UtcOffsetMinutes;
        }

        /// <summary>The inclusive start of the earliest cell, Unix epoch milliseconds.</summary>
        public long FromUnixMs
        {
            get => _FromUnixMs;
        }

        /// <summary>
        /// The inclusive start of the range's window, Unix epoch milliseconds: <see cref="FromUnixMs"/>, except for the
        /// month (its last 30 days: 60 half-days ending with the one holding now) and the year (the last 364 days,
        /// from local midnight), whose first week starts earlier. Cells before it are outside the window.
        /// </summary>
        public long WindowFromUnixMs
        {
            get => _WindowFromUnixMs;
        }

        /// <summary>The exclusive end of the latest cell, Unix epoch milliseconds.</summary>
        public long ToUnixMs
        {
            get => _ToUnixMs;
        }

        /// <summary>The width of one cell in milliseconds.</summary>
        public long BucketMs
        {
            get => _BucketMs;
        }

        /// <summary>The number of rows: the y-axis, top to bottom.</summary>
        public int Rows
        {
            get => _Rows;
        }

        /// <summary>The number of columns: the x-axis, left to right (oldest first).</summary>
        public int Columns
        {
            get => _Columns;
        }

        /// <summary>The number of cells (<see cref="Rows"/> x <see cref="Columns"/>).</summary>
        public int CellCount
        {
            get => _Rows * _Columns;
        }

        /// <summary>
        /// How many consecutive columns share one label: 2 for half-day columns (a weekday's AM and PM), else 1.
        /// Surfaces label only the first column of a group, spanning the group where they can.
        /// </summary>
        public int ColumnGroup
        {
            get => _ColumnKind == ColumnKindHalfDay ? 2 : 1;
        }

        /// <summary>How column (x) labels are formatted: one of the <c>ColumnKind*</c> constants.</summary>
        public string ColumnKind
        {
            get => _ColumnKind;
        }

        /// <summary>How row (y) labels are formatted: one of the <c>RowKind*</c> constants (also the y caption).</summary>
        public string RowKind
        {
            get => _RowKind;
        }

        /// <summary>
        /// What a column is, for the x caption: "5m" (5 minutes starting), "1h" (hour of day), "1d" (day), "halfday"
        /// (day of the week, AM and PM), "1w" (week starting), "month" (week, labeled by month), or "period".
        /// </summary>
        public string ColumnUnit
        {
            get => _ColumnUnit;
        }

        /// <summary>The instant each column's label describes, Unix epoch milliseconds, left to right.</summary>
        public IReadOnlyList<long> ColumnTimesUnixMs
        {
            get => _ColumnTimes;
        }

        /// <summary>The instant each row's label describes (its cell in the first column), Unix epoch milliseconds.</summary>
        public IReadOnlyList<long> RowTimesUnixMs
        {
            get => _RowTimes;
        }

        /// <summary>Each column's label in the invariant culture (see <see cref="FormatColumnLabel"/>).</summary>
        public IReadOnlyList<string> ColumnLabels
        {
            get => _ColumnLabels;
        }

        /// <summary>Each row's label in the invariant culture (see <see cref="FormatRowLabel"/>).</summary>
        public IReadOnlyList<string> RowLabels
        {
            get => _RowLabels;
        }

        /// <summary>
        /// The columns that may carry a label, ascending: every column; the AM column of each weekday for half-days;
        /// and for the year the first column, each column where a month starts, and the last column. Thin them with
        /// <see cref="LabeledColumns"/>.
        /// </summary>
        public IReadOnlyList<int> ColumnLabelAnchors
        {
            get => _Anchors;
        }

        /// <summary>Each cell's start, Unix epoch milliseconds, in display order (row-major).</summary>
        public IReadOnlyList<long> CellStartsUnixMs
        {
            get => _CellStarts;
        }

        /// <summary>Whether each cell, in display order, is outside the window (see <see cref="IsOutsideWindow"/>).</summary>
        public IReadOnlyList<bool> OutsideWindow
        {
            get => _Outside;
        }

        /// <summary>The English axis caption, for example "Columns: hour of day · Rows: minute of the hour".</summary>
        public string Caption
        {
            get => "Columns: " + ColumnUnitText(_ColumnUnit) + " · Rows: " + RowKindText(_RowKind);
        }

        #endregion

        #region Private-Members

        private const long MinuteMs = 60_000L;
        private const long HourMs = 60L * MinuteMs;
        private const long DayMs = 24L * HourMs;
        private const long WeekMs = 7L * DayMs;
        private const int MonthWindowBuckets = 60;
        private const int YearWindowDays = 364;
        private static readonly int[] _Strides = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 };

        private readonly string _Range;
        private readonly long _NowUnixMs;
        private readonly int _UtcOffsetMinutes;
        private readonly long _FromUnixMs;
        private readonly long _WindowFromUnixMs;
        private readonly long _ToUnixMs;
        private readonly long _BucketMs;
        private readonly int _Rows;
        private readonly int _Columns;
        private readonly bool _TimeAcross;
        private readonly string _ColumnKind;
        private readonly string _RowKind;
        private readonly string _ColumnUnit;
        private readonly long[] _ColumnTimes;
        private readonly long[] _RowTimes;
        private readonly string[] _ColumnLabels;
        private readonly string[] _RowLabels;
        private readonly int[] _Anchors;
        private readonly long[] _CellStarts;
        private readonly bool[] _Outside;

        #endregion

        #region Constructors-and-Factories

        // timeAcross: true when columns are periods and rows steps within them (time runs down each column, then to
        // the next column); false for the month calendar (time runs along each row, then to the next row).
        private UsageHeatmapLayout(string range, long now, int offsetMinutes, long from, long windowFrom, long bucketMs, int rows, int columns, bool timeAcross, string columnKind, string rowKind, string columnUnit)
        {
            _Range = range;
            _NowUnixMs = now;
            _UtcOffsetMinutes = offsetMinutes;
            _FromUnixMs = from;
            _WindowFromUnixMs = Math.Max(from, windowFrom);
            _BucketMs = bucketMs;
            _Rows = rows;
            _Columns = columns;
            _TimeAcross = timeAcross;
            _ToUnixMs = from + ((long)rows * columns * bucketMs);
            _ColumnKind = columnKind;
            _RowKind = rowKind;
            _ColumnUnit = columnUnit;

            _CellStarts = new long[rows * columns];
            _Outside = new bool[rows * columns];
            for (int i = 0; i < _CellStarts.Length; i++)
            {
                long start = from + ((long)OrdinalOf(i) * bucketMs);
                _CellStarts[i] = start;
                _Outside[i] = start < _WindowFromUnixMs || start > now;
            }

            _RowTimes = new long[rows];
            for (int r = 0; r < rows; r++) _RowTimes[r] = _CellStarts[r * columns];

            List<int> anchors = new List<int>();
            _ColumnTimes = new long[columns];
            for (int c = 0; c < columns; c++)
            {
                _ColumnTimes[c] = _CellStarts[c];
                if (columnKind == ColumnKindMonth)
                {
                    // A week column carries a month label where a month starts in it (on a day inside the window).
                    long first = Math.Max(_CellStarts[c], _WindowFromUnixMs);
                    _ColumnTimes[c] = first;
                    for (int d = 0; d < rows; d++)
                    {
                        long day = _CellStarts[(d * columns) + c];
                        if (day >= _WindowFromUnixMs && Local(day).Day == 1)
                        {
                            _ColumnTimes[c] = day;
                            anchors.Add(c);
                            break;
                        }
                    }

                    if ((c == 0 || c == columns - 1) && (anchors.Count == 0 || anchors[anchors.Count - 1] != c)) anchors.Add(c);
                }
                else if (c % ColumnGroup == 0)
                {
                    anchors.Add(c);
                }
            }

            _Anchors = anchors.ToArray();
            _ColumnLabels = new string[columns];
            for (int c = 0; c < columns; c++) _ColumnLabels[c] = FormatColumnLabel(c, CultureInfo.InvariantCulture);
            _RowLabels = new string[rows];
            for (int r = 0; r < rows; r++) _RowLabels[r] = FormatRowLabel(r, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Computes the layout for a range at a fixed local UTC offset.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="nowUnixMs">The reference time, Unix epoch milliseconds.</param>
        /// <param name="utcOffsetMinutes">The viewer's offset from UTC in minutes, east positive (clamped to -840..840).</param>
        /// <param name="earliestUnixMs">For <see cref="UsageRange.All"/>, the oldest matching event (0 when none, which
        /// falls back to the day layout); ignored by every other range.</param>
        /// <returns>The layout.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="range"/> is not a defined range.</exception>
        public static UsageHeatmapLayout Compute(UsageRange range, long nowUnixMs, int utcOffsetMinutes, long earliestUnixMs = 0)
        {
            if (!Enum.IsDefined(typeof(UsageRange), range))
            {
                throw new ArgumentOutOfRangeException(nameof(range), range, "Unknown usage range.");
            }

            int offset = Math.Clamp(utcOffsetMinutes, -840, 840);
            string key = UsageWindow.KeyOf(range);
            switch (range)
            {
                case UsageRange.Hour:
                    return Periods(key, nowUnixMs, offset, MinuteMs, 5L * MinuteMs, 12, ColumnKindMinuteOfHour, RowKindMinute, "5m");
                case UsageRange.Day:
                    return Periods(key, nowUnixMs, offset, 15L * MinuteMs, HourMs, 24, ColumnKindHour, RowKindMinuteOfHour, "1h");
                case UsageRange.Week:
                    return Periods(key, nowUnixMs, offset, 2L * HourMs, DayMs, 7, ColumnKindWeekDate, RowKindClock, "1d");
                case UsageRange.Quarter:
                    return Periods(key, nowUnixMs, offset, DayMs, WeekMs, 13, ColumnKindDate, RowKindWeekday, "1w");
                case UsageRange.Month:
                    {
                        // A calendar of Monday-start weeks x 14 half-days holding the 30-day window (60 half-days
                        // ending with the one holding now): 5 weeks, or 6 when the window starts before them (on a
                        // Monday, or a Tuesday before noon).
                        long halfDay = 12L * HourMs;
                        long to = Floor(nowUnixMs, WeekMs, offset) + WeekMs;
                        long windowFrom = Floor(nowUnixMs, halfDay, offset) - ((MonthWindowBuckets - 1) * halfDay);
                        int rows = windowFrom >= to - (5L * WeekMs) ? 5 : 6;
                        return new UsageHeatmapLayout(key, nowUnixMs, offset, to - (rows * WeekMs), windowFrom, halfDay, rows, 14, false, ColumnKindHalfDay, RowKindWeek, "halfday");
                    }

                case UsageRange.Year:
                    {
                        // Daily cells for the last 364 days (from local midnight 363 days ago through today), in
                        // Monday-start week columns: 52 when today is a Sunday, else 53.
                        long to = Floor(nowUnixMs, WeekMs, offset) + WeekMs;
                        long windowFrom = Floor(nowUnixMs, DayMs, offset) - ((YearWindowDays - 1) * DayMs);
                        long from = Floor(windowFrom, WeekMs, offset);
                        int columns = (int)((to - from) / WeekMs);
                        return new UsageHeatmapLayout(key, nowUnixMs, offset, from, windowFrom, DayMs, 7, columns, true, ColumnKindMonth, RowKindWeekday, "month");
                    }

                default:
                    {
                        if (earliestUnixMs <= 0 || earliestUnixMs >= nowUnixMs)
                        {
                            UsageHeatmapLayout day = Compute(UsageRange.Day, nowUnixMs, offset, 0);
                            return new UsageHeatmapLayout(key, nowUnixMs, offset, day.FromUnixMs, day.FromUnixMs, day.BucketMs, day.Rows, day.Columns, true, day.ColumnKind, day.RowKind, day.ColumnUnit);
                        }

                        long bucketMs = UsageWindow.NiceBucketMs((nowUnixMs - earliestUnixMs + 89L) / 90L);
                        AllShape(bucketMs, out long periodMs, out string rowKind);
                        long anchorMs = periodMs >= WeekMs ? WeekMs : periodMs;
                        long to = Floor(nowUnixMs, anchorMs, offset) + anchorMs;
                        long first = Floor(earliestUnixMs, anchorMs, offset);
                        int columns = (int)Math.Max(1, (to - first + periodMs - 1) / periodMs);
                        long from = to - (columns * periodMs);
                        string columnKind = periodMs >= WeekMs ? (bucketMs >= WeekMs ? ColumnKindFullDate : ColumnKindDate)
                            : periodMs == DayMs ? ColumnKindWeekDate
                            : (to - from <= DayMs ? ColumnKindTime : ColumnKindDateTime);
                        return new UsageHeatmapLayout(key, nowUnixMs, offset, from, from, bucketMs, (int)(periodMs / bucketMs), columns, true, columnKind, rowKind, "period");
                    }
            }
        }

        /// <summary>
        /// Computes the layout for a range in a time zone, at the zone's offset at <paramref name="nowUnixMs"/>.
        /// </summary>
        /// <param name="range">The range.</param>
        /// <param name="nowUnixMs">The reference time, Unix epoch milliseconds.</param>
        /// <param name="zone">The viewer's time zone; null uses <see cref="TimeZoneInfo.Local"/>.</param>
        /// <param name="earliestUnixMs">For <see cref="UsageRange.All"/>, the oldest matching event, or 0.</param>
        /// <returns>The layout.</returns>
        public static UsageHeatmapLayout Compute(UsageRange range, long nowUnixMs, TimeZoneInfo? zone, long earliestUnixMs = 0)
        {
            return Compute(range, nowUnixMs, OffsetMinutesOf(zone ?? TimeZoneInfo.Local, nowUnixMs), earliestUnixMs);
        }

        #endregion

        #region Public-Methods

        /// <summary>The offset in minutes (east positive) of a time zone at an instant.</summary>
        /// <param name="zone">The time zone. Required.</param>
        /// <param name="unixMs">The instant, Unix epoch milliseconds.</param>
        /// <returns>The offset in minutes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="zone"/> is null.</exception>
        public static int OffsetMinutesOf(TimeZoneInfo zone, long unixMs)
        {
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            return (int)Math.Round(zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(unixMs)).TotalMinutes);
        }

        /// <summary>
        /// Converts a JavaScript <c>Date.getTimezoneOffset()</c> value (minutes to add to local time to get UTC, so
        /// UTC-5 is 300) to the east-positive offset this class uses, clamped to -840..840.
        /// </summary>
        /// <param name="tzOffsetMinutes">The JavaScript offset.</param>
        /// <returns>The east-positive offset.</returns>
        public static int FromJavaScriptOffset(int tzOffsetMinutes)
        {
            return -Math.Clamp(tzOffsetMinutes, -840, 840);
        }

        /// <summary>The start of a cell, Unix epoch milliseconds.</summary>
        /// <param name="index">The cell index in display order (row x <see cref="Columns"/> + column).</param>
        /// <returns>The cell start.</returns>
        public long CellStartUnixMs(int index)
        {
            return _CellStarts[index];
        }

        /// <summary>The display index of a cell (row x <see cref="Columns"/> + column).</summary>
        /// <param name="row">The row (y).</param>
        /// <param name="column">The column (x).</param>
        /// <returns>The index.</returns>
        public int IndexAt(int row, int column)
        {
            return (row * _Columns) + column;
        }

        /// <summary>The display index of the cell holding a timestamp, or -1 when it is outside the grid or before the window.</summary>
        /// <param name="unixMs">The timestamp, Unix epoch milliseconds.</param>
        /// <returns>The cell index, or -1.</returns>
        public int IndexOf(long unixMs)
        {
            if (unixMs < _WindowFromUnixMs || unixMs >= _ToUnixMs) return -1;
            return IndexOfOrdinal((int)((unixMs - _FromUnixMs) / _BucketMs));
        }

        /// <summary>The cells' display indices in chronological order (oldest first).</summary>
        /// <returns>The indices.</returns>
        public IEnumerable<int> CellsInTimeOrder()
        {
            for (int k = 0; k < CellCount; k++) yield return IndexOfOrdinal(k);
        }

        /// <summary>Whether a cell starts after <see cref="NowUnixMs"/>.</summary>
        /// <param name="index">The cell index.</param>
        /// <returns>True for a future cell.</returns>
        public bool IsFuture(int index)
        {
            return _CellStarts[index] > _NowUnixMs;
        }

        /// <summary>
        /// Whether a cell is outside the range's window: before <see cref="WindowFromUnixMs"/> or after now. Surfaces
        /// draw it blank ("no data") and do not make it clickable.
        /// </summary>
        /// <param name="index">The cell index.</param>
        /// <returns>True for a cell outside the window.</returns>
        public bool IsOutsideWindow(int index)
        {
            return _Outside[index];
        }

        /// <summary>
        /// A column's (x) label in local time, by <see cref="ColumnKind"/>: ":05" (minuteofhour), "14" (hour), "14:30"
        /// (time), "11/5 14:00" (datetime), "Thu 11/5" (weekdate), the weekday for both half-days (halfday), "11/2"
        /// (date), "Nov" (month), or "2026-11-02" (fulldate). The culture supplies names and the month-day order.
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="culture">The culture; null uses the invariant culture.</param>
        /// <returns>The label.</returns>
        public string FormatColumnLabel(int column, CultureInfo? culture = null)
        {
            CultureInfo ci = culture ?? CultureInfo.InvariantCulture;
            DateTime local = Local(_ColumnTimes[column]);
            switch (_ColumnKind)
            {
                case ColumnKindMinuteOfHour: return ":" + local.Minute.ToString("00", CultureInfo.InvariantCulture);
                case ColumnKindHour: return local.Hour.ToString("00", CultureInfo.InvariantCulture);
                case ColumnKindTime: return local.ToString("HH:mm", CultureInfo.InvariantCulture);
                case ColumnKindDateTime: return local.ToString(MonthDayPattern(ci), ci) + " " + local.ToString("HH:mm", CultureInfo.InvariantCulture);
                case ColumnKindWeekDate: return local.ToString("ddd", ci) + " " + local.ToString(MonthDayPattern(ci), ci);
                case ColumnKindHalfDay: return local.ToString("ddd", ci);
                case ColumnKindDate: return local.ToString(MonthDayPattern(ci), ci);
                case ColumnKindMonth: return local.ToString("MMM", ci);
                default: return ci == CultureInfo.InvariantCulture ? local.ToString("yyyy-MM-dd", ci) : local.ToString("d", ci);
            }
        }

        /// <summary>
        /// A row's (y) label, by <see cref="RowKind"/>: "+2m" (minute), ":15" (minuteofhour), "+2h" or "+1h30m" (hours),
        /// "02:00" (clock), an abbreviated weekday (weekday), the week's start date (week), or "+2w" (weeks).
        /// </summary>
        /// <param name="row">The row index.</param>
        /// <param name="culture">The culture; null uses the invariant culture.</param>
        /// <returns>The label.</returns>
        public string FormatRowLabel(int row, CultureInfo? culture = null)
        {
            CultureInfo ci = culture ?? CultureInfo.InvariantCulture;
            DateTime local = Local(_RowTimes[row]);
            long minutes = (_TimeAcross ? row * _BucketMs : 0) / MinuteMs;
            switch (_RowKind)
            {
                case RowKindMinute: return "+" + minutes.ToString(CultureInfo.InvariantCulture) + "m";
                case RowKindMinuteOfHour: return ":" + local.Minute.ToString("00", CultureInfo.InvariantCulture);
                case RowKindHours:
                    return minutes % 60 == 0
                        ? "+" + (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h"
                        : "+" + (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture) + "m";
                case RowKindClock: return local.ToString("HH:mm", CultureInfo.InvariantCulture);
                case RowKindWeekday: return local.ToString("ddd", ci);
                case RowKindWeek: return ci == CultureInfo.InvariantCulture ? local.ToString("yyyy-MM-dd", ci) : local.ToString("d", ci);
                default: return "+" + (minutes / (7L * 24L * 60L)).ToString(CultureInfo.InvariantCulture) + "w";
            }
        }

        /// <summary>
        /// Which columns to label so labels never collide, when one label (with its spacing) needs
        /// <paramref name="minGap"/> columns: see <see cref="ThinLabels"/> over <see cref="ColumnLabelAnchors"/>.
        /// </summary>
        /// <param name="minGap">The columns one label needs (at least 1).</param>
        /// <returns>The column indices to label, ascending; the last is drawn right-aligned to the grid's end.</returns>
        public List<int> LabeledColumns(int minGap)
        {
            return ThinLabels(_Anchors, _Columns, ColumnGroup, minGap, _ColumnKind != ColumnKindMonth);
        }

        /// <summary>
        /// Thins x-axis labels so none collide, keeping the first and the last. A label at column a is drawn from a's left
        /// edge over <paramref name="minGap"/> columns; the last one ends at the grid's right edge instead. Evenly spaced
        /// anchors are kept every 1st, 2nd, 3rd, 4th, 6th, 8th, 12th, ... anchor so labels fall on round values;
        /// uneven anchors (month starts) are kept greedily. Stride labels too close to the last are dropped; when even
        /// the first and last would collide, only the first is kept.
        /// </summary>
        /// <param name="anchors">The columns that may carry a label, ascending, starting at 0.</param>
        /// <param name="columns">The column count.</param>
        /// <param name="group">Columns per label group (2 for half-days); the last label covers the last group.</param>
        /// <param name="minGap">The columns one label needs (at least 1).</param>
        /// <param name="even">True when the anchors are evenly spaced.</param>
        /// <returns>The columns to label, ascending.</returns>
        public static List<int> ThinLabels(IReadOnlyList<int> anchors, int columns, int group, int minGap, bool even)
        {
            List<int> picked = new List<int>();
            if (anchors == null || anchors.Count == 0 || columns <= 0) return picked;
            int gap = Math.Max(1, minGap);
            group = Math.Max(1, group);
            int last = anchors[anchors.Count - 1];
            if (even)
            {
                int spacing = anchors.Count > 1 ? anchors[1] - anchors[0] : 1;
                int stride = _Strides[_Strides.Length - 1];
                foreach (int s in _Strides)
                {
                    if (s * spacing >= gap)
                    {
                        stride = s;
                        break;
                    }
                }

                for (int i = 0; i < anchors.Count; i += stride) picked.Add(anchors[i]);
            }
            else
            {
                picked.Add(anchors[0]);
                for (int i = 1; i < anchors.Count; i++)
                {
                    if (anchors[i] - picked[picked.Count - 1] >= gap) picked.Add(anchors[i]);
                }
            }

            if (last == anchors[0]) return picked;
            if (picked.Count > 1 && picked[picked.Count - 1] == last) picked.RemoveAt(picked.Count - 1);

            // The last label is right-aligned, so it ends where the grid does: it needs its own width plus the width
            // of the label before it between the two anchors.
            int need = (2 * gap) - group;
            while (picked.Count > 1 && last - picked[picked.Count - 1] < need) picked.RemoveAt(picked.Count - 1);
            if (last - picked[picked.Count - 1] >= need) picked.Add(last);
            return picked;
        }

        /// <summary>
        /// Picks which of <paramref name="count"/> labels to draw so at most about <paramref name="maxLabels"/> show:
        /// every 1st, 2nd, 3rd, 4th, 6th, 8th, 12th, ... label, plus the last one when <paramref name="includeLast"/>
        /// (dropping the stride label just before it when the two would crowd). The first is always drawn. Used for
        /// the y-axis.
        /// </summary>
        /// <param name="count">The number of rows.</param>
        /// <param name="maxLabels">The most labels that fit (at least 1).</param>
        /// <param name="includeLast">Whether the last row is always labeled.</param>
        /// <returns>The indices to label, ascending.</returns>
        public static List<int> LabeledIndices(int count, int maxLabels, bool includeLast)
        {
            List<int> picked = new List<int>();
            if (count <= 0) return picked;
            int max = Math.Max(1, maxLabels);
            int stride = 1;
            foreach (int step in _Strides)
            {
                stride = step;
                if ((count + step - 1) / step <= max) break;
            }

            for (int i = 0; i < count; i += stride) picked.Add(i);
            if (includeLast && picked[picked.Count - 1] != count - 1)
            {
                if (picked.Count > 1 && (count - 1) - picked[picked.Count - 1] < stride)
                {
                    picked.RemoveAt(picked.Count - 1);
                }

                picked.Add(count - 1);
            }

            return picked;
        }

        /// <summary>The English name of a column unit for the caption ("hour of day").</summary>
        /// <param name="columnUnit">The <see cref="ColumnUnit"/> value.</param>
        /// <returns>The text.</returns>
        public static string ColumnUnitText(string columnUnit)
        {
            switch (columnUnit)
            {
                case "5m": return "5 minutes starting";
                case "1h": return "hour of day";
                case "1d": return "day";
                case "halfday": return "day of the week (AM, PM)";
                case "1w": return "week starting";
                case "month": return "week (by month)";
                default: return "period starting";
            }
        }

        /// <summary>The English name of a row kind for the caption ("minute of the hour").</summary>
        /// <param name="rowKind">The <see cref="RowKind"/> value.</param>
        /// <returns>The text.</returns>
        public static string RowKindText(string rowKind)
        {
            switch (rowKind)
            {
                case RowKindMinute: return "minutes after the column start";
                case RowKindMinuteOfHour: return "minute of the hour";
                case RowKindHours: return "hours after the column start";
                case RowKindClock: return "time of day";
                case RowKindWeekday: return "day of the week";
                case RowKindWeek: return "week starting";
                default: return "weeks after the column start";
            }
        }

        #endregion

        #region Private-Methods

        // Columns of local-time periods, ending with the one holding now, each split into rows of one bucket.
        private static UsageHeatmapLayout Periods(string key, long now, int offset, long bucketMs, long periodMs, int columns, string columnKind, string rowKind, string columnUnit)
        {
            long to = Floor(now, periodMs, offset) + periodMs;
            long from = to - (columns * periodMs);
            return new UsageHeatmapLayout(key, now, offset, from, from, bucketMs, (int)(periodMs / bucketMs), columns, true, columnKind, rowKind, columnUnit);
        }

        // The all-time column period and row kind for a bucket width, keeping a column to 2-7 rows so the grid
        // stays wider than tall.
        private static void AllShape(long bucketMs, out long periodMs, out string rowKind)
        {
            if (bucketMs <= MinuteMs) { periodMs = 5L * MinuteMs; rowKind = RowKindMinute; }
            else if (bucketMs <= 5L * MinuteMs) { periodMs = 30L * MinuteMs; rowKind = RowKindMinute; }
            else if (bucketMs <= 15L * MinuteMs) { periodMs = HourMs; rowKind = RowKindMinuteOfHour; }
            else if (bucketMs <= 30L * MinuteMs) { periodMs = 2L * HourMs; rowKind = RowKindHours; }
            else if (bucketMs <= HourMs) { periodMs = 6L * HourMs; rowKind = RowKindHours; }
            else if (bucketMs <= 2L * HourMs) { periodMs = 12L * HourMs; rowKind = RowKindHours; }
            else if (bucketMs < DayMs) { periodMs = DayMs; rowKind = RowKindClock; }
            else if (bucketMs < WeekMs) { periodMs = WeekMs; rowKind = RowKindWeekday; }
            else { periodMs = 4L * WeekMs; rowKind = RowKindWeeks; }

            periodMs = Math.Max(periodMs, bucketMs);
        }

        // The chronological position of a display index, and back.
        private int OrdinalOf(int index)
        {
            if (!_TimeAcross) return index;
            int row = index / _Columns, column = index % _Columns;
            return (column * _Rows) + row;
        }

        private int IndexOfOrdinal(int ordinal)
        {
            if (!_TimeAcross) return ordinal;
            return ((ordinal % _Rows) * _Columns) + (ordinal / _Rows);
        }

        // The start of the local-time unit holding a timestamp (a unit dividing a day, or a week starting Monday) at a
        // fixed offset.
        private static long Floor(long unixMs, long unitMs, int offsetMinutes)
        {
            long shift = offsetMinutes * MinuteMs;
            long local = unixMs + shift;
            if (unitMs == WeekMs)
            {
                long day = FloorDiv(local, DayMs);
                long sinceMonday = Mod(day + 3, 7); // 1970-01-01 was a Thursday.
                return ((day - sinceMonday) * DayMs) - shift;
            }

            return (FloorDiv(local, unitMs) * unitMs) - shift;
        }

        private static long FloorDiv(long a, long b)
        {
            long q = a / b;
            return (a % b != 0 && (a < 0) != (b < 0)) ? q - 1 : q;
        }

        private static long Mod(long a, long b)
        {
            long m = a % b;
            return m < 0 ? m + b : m;
        }

        // The culture's numeric month-day order without the year ("M/d" invariant, "dd.MM" German, "MM-dd" ISO).
        private static string MonthDayPattern(CultureInfo ci)
        {
            if (ci == CultureInfo.InvariantCulture) return "M/d";
            string pattern = Regex.Replace(ci.DateTimeFormat.ShortDatePattern, "[^dM]*y+[^dM]*", string.Empty).Trim();
            return pattern.Length == 0 ? "M/d" : pattern;
        }

        private DateTime Local(long unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs + (_UtcOffsetMinutes * MinuteMs)).UtcDateTime;
        }

        #endregion
    }
}
