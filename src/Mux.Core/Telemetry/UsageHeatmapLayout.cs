namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// The clock-aligned grid of the token heatmap, shared by every surface (the web dashboard and VS Code through
    /// <c>GET /v1.0/api/usage/heatmap</c>, the desktop app and the terminal in process). Each row is one local-time
    /// period and each column one bucket within it:
    /// hour = 4 quarter-hour rows x 15 one-minute columns; day = 24 hour rows x 4 quarter-hour columns;
    /// week = 7 day rows x 12 two-hour columns; month = a 5-week calendar, 5 week rows (Monday start) x 14 half-day
    /// columns (Mon AM, Mon PM, ... Sun PM); quarter = 13 week rows (Monday start) x 7 weekday columns; year = 4 rows
    /// of 13 weeks x 13 weekly columns; all = rows and columns chosen from its bucket width, with as many rows as the
    /// history needs. Rows align to the viewer's local time at a fixed UTC offset (the offset at "now"), and the last
    /// row holds "now", so later cells in it are in the future (<see cref="IsFuture"/>). The month calendar covers
    /// only the last 30 days (<see cref="WindowFromUnixMs"/>), so its cells before that are outside the window too;
    /// surfaces draw every cell <see cref="IsOutsideWindow"/> reports as blank and not clickable.
    /// </summary>
    public sealed class UsageHeatmapLayout
    {
        #region Public-Members

        /// <summary>Row labels show a time ("14:00").</summary>
        public const string RowKindTime = "time";

        /// <summary>Row labels show a date and time ("11-10 06:00").</summary>
        public const string RowKindDateTime = "datetime";

        /// <summary>Row labels show a weekday and date ("Mon 11-10").</summary>
        public const string RowKindWeekDate = "weekdate";

        /// <summary>Row labels show a full date ("2026-11-09").</summary>
        public const string RowKindDate = "date";

        /// <summary>Columns are minutes after the row start ("+5m").</summary>
        public const string ColumnKindMinute = "minute";

        /// <summary>Columns are minutes of the hour (":00", ":15").</summary>
        public const string ColumnKindMinuteOfHour = "minuteofhour";

        /// <summary>Columns are hours after the row start ("+2h").</summary>
        public const string ColumnKindHours = "hours";

        /// <summary>Columns are times of day ("00:00", "02:00").</summary>
        public const string ColumnKindClock = "clock";

        /// <summary>Columns are weekdays, Monday first.</summary>
        public const string ColumnKindWeekday = "weekday";

        /// <summary>Columns are weeks after the row start ("+4w").</summary>
        public const string ColumnKindWeeks = "weeks";

        /// <summary>Columns are half-days, Monday AM first (two columns per weekday, labeled once per day).</summary>
        public const string ColumnKindHalfDay = "halfday";

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

        /// <summary>The inclusive start of the first cell, Unix epoch milliseconds.</summary>
        public long FromUnixMs
        {
            get => _FromUnixMs;
        }

        /// <summary>
        /// The inclusive start of the first cell inside the range's window, Unix epoch milliseconds. Equals
        /// <see cref="FromUnixMs"/> except for the month calendar, whose 5 weeks start before its last 30 days.
        /// </summary>
        public long WindowFromUnixMs
        {
            get => _WindowFromUnixMs;
        }

        /// <summary>The exclusive end of the last cell, Unix epoch milliseconds.</summary>
        public long ToUnixMs
        {
            get => _ToUnixMs;
        }

        /// <summary>The width of one cell in milliseconds.</summary>
        public long BucketMs
        {
            get => _BucketMs;
        }

        /// <summary>The span of one row in milliseconds (<see cref="Columns"/> x <see cref="BucketMs"/>).</summary>
        public long RowMs
        {
            get => _BucketMs * _Columns;
        }

        /// <summary>The number of rows, oldest first.</summary>
        public int Rows
        {
            get => _Rows;
        }

        /// <summary>The number of columns per row.</summary>
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

        /// <summary>How row labels are formatted: one of the <c>RowKind*</c> constants.</summary>
        public string RowKind
        {
            get => _RowKind;
        }

        /// <summary>What one row spans, for the axis caption: "15m", "1h", "6h", "1d", "1w", or "13w".</summary>
        public string RowUnit
        {
            get => _RowUnit;
        }

        /// <summary>What a column means within a row: one of the <c>ColumnKind*</c> constants.</summary>
        public string ColumnKind
        {
            get => _ColumnKind;
        }

        /// <summary>Each row's start, Unix epoch milliseconds, oldest first.</summary>
        public IReadOnlyList<long> RowStartsUnixMs
        {
            get => _RowStarts;
        }

        /// <summary>Each row's label in the invariant culture (see <see cref="FormatRowLabel"/>).</summary>
        public IReadOnlyList<string> RowLabels
        {
            get => _RowLabels;
        }

        /// <summary>Each column's label in the invariant culture (see <see cref="FormatColumnLabel"/>).</summary>
        public IReadOnlyList<string> ColumnLabels
        {
            get => _ColumnLabels;
        }

        /// <summary>The English axis caption, for example "Rows: day starting · Columns: time of day".</summary>
        public string Caption
        {
            get => "Rows: " + RowUnitText(_RowUnit) + " · Columns: " + ColumnKindText(_ColumnKind);
        }

        #endregion

        #region Private-Members

        private const long MinuteMs = 60_000L;
        private const long HourMs = 60L * MinuteMs;
        private const long DayMs = 24L * HourMs;
        private const long WeekMs = 7L * DayMs;
        private const int MonthRows = 5;
        private const int MonthWindowBuckets = 60;

        private readonly string _Range;
        private readonly long _NowUnixMs;
        private readonly int _UtcOffsetMinutes;
        private readonly long _FromUnixMs;
        private readonly long _WindowFromUnixMs;
        private readonly long _ToUnixMs;
        private readonly long _BucketMs;
        private readonly int _Rows;
        private readonly int _Columns;
        private readonly string _RowKind;
        private readonly string _RowUnit;
        private readonly string _ColumnKind;
        private readonly long[] _RowStarts;
        private readonly string[] _RowLabels;
        private readonly string[] _ColumnLabels;

        #endregion

        #region Constructors-and-Factories

        private UsageHeatmapLayout(string range, long now, int offsetMinutes, long from, long windowFrom, long bucketMs, int rows, int columns, string rowKind, string rowUnit, string columnKind)
        {
            _Range = range;
            _NowUnixMs = now;
            _UtcOffsetMinutes = offsetMinutes;
            _FromUnixMs = from;
            _WindowFromUnixMs = Math.Max(from, windowFrom);
            _BucketMs = bucketMs;
            _Rows = rows;
            _Columns = columns;
            _ToUnixMs = from + (long)rows * columns * bucketMs;
            _RowKind = rowKind;
            _RowUnit = rowUnit;
            _ColumnKind = columnKind;
            _RowStarts = new long[rows];
            _RowLabels = new string[rows];
            for (int r = 0; r < rows; r++)
            {
                _RowStarts[r] = from + (long)r * columns * bucketMs;
                _RowLabels[r] = FormatRowLabel(r, CultureInfo.InvariantCulture);
            }

            _ColumnLabels = new string[columns];
            for (int c = 0; c < columns; c++)
            {
                _ColumnLabels[c] = FormatColumnLabel(c, CultureInfo.InvariantCulture);
            }
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
            long bucketMs;
            int rows;
            if (range == UsageRange.All)
            {
                if (earliestUnixMs <= 0 || earliestUnixMs >= nowUnixMs)
                {
                    return Compute(UsageRange.Day, nowUnixMs, offset, 0);
                }

                bucketMs = UsageWindow.NiceBucketMs((nowUnixMs - earliestUnixMs + 89L) / 90L);
                rows = 0;
            }
            else
            {
                UsageWindow.Bucketing(UsageWindow.KeyOf(range), out bucketMs, out _);
                rows = range switch
                {
                    UsageRange.Hour => 4,
                    UsageRange.Day => 24,
                    UsageRange.Week => 7,
                    UsageRange.Month => MonthRows,
                    UsageRange.Quarter => 13,
                    _ => 4
                };
            }

            Shape(bucketMs, out long anchorMs, out long rowMs, out string rowUnit, out string columnKind, out string rowKind);
            if (range == UsageRange.Month)
            {
                // A 5-week calendar: weeks starting Monday (as the quarter's rows) x 14 half-days.
                anchorMs = WeekMs; rowMs = WeekMs; rowUnit = "1w"; columnKind = ColumnKindHalfDay; rowKind = RowKindDate;
            }

            int columns = (int)(rowMs / bucketMs);
            long to = Floor(nowUnixMs, anchorMs, offset) + anchorMs;
            if (range == UsageRange.All)
            {
                long first = Floor(earliestUnixMs, anchorMs, offset);
                rows = (int)Math.Max(1, (to - first + rowMs - 1) / rowMs);
                if (rowMs < DayMs && rows * rowMs > DayMs)
                {
                    rowKind = RowKindDateTime;
                }
            }

            long from = to - (rows * rowMs);

            // The month keeps its 30-day window (60 half-days ending with the one holding now); the calendar's earlier
            // cells are outside it. Early in a week (Monday, or Tuesday morning) the window starts before the first
            // row, so its oldest one to three half-days are not shown.
            long windowFrom = range == UsageRange.Month
                ? Floor(nowUnixMs, bucketMs, offset) - ((MonthWindowBuckets - 1) * bucketMs)
                : from;
            return new UsageHeatmapLayout(UsageWindow.KeyOf(range), nowUnixMs, offset, from, windowFrom, bucketMs, rows, columns, rowKind, rowUnit, columnKind);
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
        /// <param name="index">The cell index, row-major (row x <see cref="Columns"/> + column).</param>
        /// <returns>The cell start.</returns>
        public long CellStartUnixMs(int index)
        {
            return _FromUnixMs + ((long)index * _BucketMs);
        }

        /// <summary>The index of the cell holding a timestamp, or -1 when it is outside the grid or before the window.</summary>
        /// <param name="unixMs">The timestamp, Unix epoch milliseconds.</param>
        /// <returns>The row-major cell index, or -1.</returns>
        public int IndexOf(long unixMs)
        {
            if (unixMs < _WindowFromUnixMs || unixMs >= _ToUnixMs) return -1;
            return (int)((unixMs - _FromUnixMs) / _BucketMs);
        }

        /// <summary>Whether a cell starts after <see cref="NowUnixMs"/> (the rest of the current row).</summary>
        /// <param name="index">The cell index.</param>
        /// <returns>True for a future cell.</returns>
        public bool IsFuture(int index)
        {
            return CellStartUnixMs(index) > _NowUnixMs;
        }

        /// <summary>
        /// Whether a cell is outside the range's window: before <see cref="WindowFromUnixMs"/> or after now. Surfaces
        /// draw it blank ("no data") and do not make it clickable.
        /// </summary>
        /// <param name="index">The cell index.</param>
        /// <returns>True for a cell outside the window.</returns>
        public bool IsOutsideWindow(int index)
        {
            return CellStartUnixMs(index) < _WindowFromUnixMs || IsFuture(index);
        }

        /// <summary>
        /// A row's label in local time: "HH:mm" (time), "MM-dd HH:mm" (datetime), "ddd MM-dd" (weekdate), or
        /// "yyyy-MM-dd" (date). The culture supplies weekday names.
        /// </summary>
        /// <param name="row">The row index.</param>
        /// <param name="culture">The culture; null uses the invariant culture.</param>
        /// <returns>The label.</returns>
        public string FormatRowLabel(int row, CultureInfo? culture = null)
        {
            CultureInfo ci = culture ?? CultureInfo.InvariantCulture;
            DateTime local = Local(_FromUnixMs + ((long)row * RowMs));
            switch (_RowKind)
            {
                case RowKindTime: return local.ToString("HH:mm", ci);
                case RowKindDateTime: return local.ToString("MM-dd HH:mm", ci);
                case RowKindWeekDate: return local.ToString("ddd", ci) + " " + local.ToString("MM-dd", ci);
                default: return local.ToString("yyyy-MM-dd", ci);
            }
        }

        /// <summary>
        /// A column's label: "+5m" (minute), ":15" (minute of the hour), "+2h" or "+1h30m" (hours), "02:00" (clock),
        /// an abbreviated weekday, Monday first (weekday; half-day columns repeat it for the AM and PM column), or
        /// "+4w" (weeks).
        /// </summary>
        /// <param name="column">The column index.</param>
        /// <param name="culture">The culture for weekday names; null uses the invariant culture.</param>
        /// <returns>The label.</returns>
        public string FormatColumnLabel(int column, CultureInfo? culture = null)
        {
            CultureInfo ci = culture ?? CultureInfo.InvariantCulture;
            long offsetMs = column * _BucketMs;
            long minutes = offsetMs / MinuteMs;
            switch (_ColumnKind)
            {
                case ColumnKindMinute:
                    return "+" + minutes.ToString(CultureInfo.InvariantCulture) + "m";
                case ColumnKindMinuteOfHour:
                    return ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
                case ColumnKindHours:
                    return minutes % 60 == 0
                        ? "+" + (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h"
                        : "+" + (minutes / 60).ToString(CultureInfo.InvariantCulture) + "h" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture) + "m";
                case ColumnKindClock:
                    return (minutes / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minutes % 60).ToString("00", CultureInfo.InvariantCulture);
                case ColumnKindWeekday:
                    return ci.DateTimeFormat.AbbreviatedDayNames[(column + 1) % 7];
                case ColumnKindHalfDay:
                    return ci.DateTimeFormat.AbbreviatedDayNames[((column / 2) + 1) % 7];
                default:
                    return "+" + (offsetMs / WeekMs).ToString(CultureInfo.InvariantCulture) + "w";
            }
        }

        /// <summary>
        /// Picks which of <paramref name="count"/> labels to draw so at most about <paramref name="maxLabels"/> show:
        /// every 1st, 2nd, 3rd, 4th, 6th, 8th, 12th, ... label, plus the last one when <paramref name="includeLast"/>
        /// (dropping the stride label just before it when the two would crowd). The first is always drawn.
        /// </summary>
        /// <param name="count">The number of rows or columns.</param>
        /// <param name="maxLabels">The most labels that fit (at least 1).</param>
        /// <param name="includeLast">Whether the last row or column is always labeled.</param>
        /// <returns>The indices to label, ascending.</returns>
        public static List<int> LabeledIndices(int count, int maxLabels, bool includeLast)
        {
            List<int> picked = new List<int>();
            if (count <= 0) return picked;
            int max = Math.Max(1, maxLabels);
            int stride = 1;
            int[] steps = { 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64 };
            foreach (int step in steps)
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

        /// <summary>
        /// Which columns to label so at most about <paramref name="maxLabels"/> show: <see cref="LabeledIndices"/> over
        /// the column groups (see <see cref="ColumnGroup"/>), so a half-day grid labels each weekday once, over its AM
        /// column.
        /// </summary>
        /// <param name="maxLabels">The most labels that fit (at least 1).</param>
        /// <returns>The column indices to label, ascending.</returns>
        public List<int> LabeledColumns(int maxLabels)
        {
            int group = ColumnGroup;
            List<int> picked = LabeledIndices((_Columns + group - 1) / group, maxLabels, false);
            for (int i = 0; i < picked.Count; i++) picked[i] *= group;
            return picked;
        }

        /// <summary>The English name of a row unit for the caption ("day starting").</summary>
        /// <param name="rowUnit">The <see cref="RowUnit"/> value.</param>
        /// <returns>The text.</returns>
        public static string RowUnitText(string rowUnit)
        {
            switch (rowUnit)
            {
                case "15m": return "15 minutes starting";
                case "1h": return "hour starting";
                case "6h": return "6 hours starting";
                case "1d": return "day starting";
                case "1w": return "week starting";
                default: return "13 weeks starting";
            }
        }

        /// <summary>The English name of a column kind for the caption ("time of day").</summary>
        /// <param name="columnKind">The <see cref="ColumnKind"/> value.</param>
        /// <returns>The text.</returns>
        public static string ColumnKindText(string columnKind)
        {
            switch (columnKind)
            {
                case ColumnKindMinute: return "minutes after the row start";
                case ColumnKindMinuteOfHour: return "minute of the hour";
                case ColumnKindHours: return "hours after the row start";
                case ColumnKindClock: return "time of day";
                case ColumnKindWeekday: return "day of the week";
                case ColumnKindHalfDay: return "day (AM, PM)";
                default: return "weeks after the row start";
            }
        }

        #endregion

        #region Private-Methods

        // The row shape for a bucket width: the alignment unit, the row span, and the caption and label kinds.
        private static void Shape(long bucketMs, out long anchorMs, out long rowMs, out string rowUnit, out string columnKind, out string rowKind)
        {
            if (bucketMs <= MinuteMs)
            {
                anchorMs = 15L * MinuteMs; rowMs = anchorMs; rowUnit = "15m"; columnKind = ColumnKindMinute; rowKind = RowKindTime;
            }
            else if (bucketMs <= 15L * MinuteMs)
            {
                anchorMs = HourMs; rowMs = HourMs; rowUnit = "1h"; columnKind = ColumnKindMinuteOfHour; rowKind = RowKindTime;
            }
            else if (bucketMs <= 30L * MinuteMs)
            {
                anchorMs = 6L * HourMs; rowMs = anchorMs; rowUnit = "6h"; columnKind = ColumnKindHours; rowKind = RowKindDateTime;
            }
            else if (bucketMs < DayMs)
            {
                anchorMs = DayMs; rowMs = DayMs; rowUnit = "1d"; columnKind = ColumnKindClock; rowKind = RowKindWeekDate;
            }
            else if (bucketMs < WeekMs)
            {
                anchorMs = WeekMs; rowMs = WeekMs; rowUnit = "1w"; columnKind = ColumnKindWeekday; rowKind = RowKindDate;
            }
            else
            {
                anchorMs = WeekMs; rowMs = 13L * WeekMs; rowUnit = "13w"; columnKind = ColumnKindWeeks; rowKind = RowKindDate;
            }
        }

        // The start of the local-time unit holding a timestamp: a quarter-hour, an hour, 6 hours, a day, or a week
        // starting Monday, at a fixed offset.
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

        private DateTime Local(long unixMs)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMs + (_UtcOffsetMinutes * MinuteMs)).UtcDateTime;
        }

        #endregion
    }
}
