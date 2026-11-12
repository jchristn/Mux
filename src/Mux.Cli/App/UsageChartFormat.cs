namespace Mux.Cli.App
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Compact, culture-invariant number and time formats shared by the usage chart views.
    /// </summary>
    public static class UsageChartFormat
    {
        #region Private-Members

        private const long MinuteMs = 60_000L;
        private const long DayMs = 24L * 60L * MinuteMs;

        #endregion

        #region Public-Methods

        /// <summary>Formats a token count: "950", "1.2k", "3.4M".</summary>
        /// <param name="value">The count.</param>
        /// <returns>The text.</returns>
        public static string Tokens(double value)
        {
            if (value >= 1_000_000) return (value / 1_000_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (value >= 1000) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats US dollars: "$0.12", "$140".</summary>
        /// <param name="value">The amount.</param>
        /// <returns>The text.</returns>
        public static string Usd(double value)
        {
            return "$" + value.ToString(value >= 100 ? "0" : "0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats milliseconds: "850" or "1.2s".</summary>
        /// <param name="value">The duration in milliseconds.</param>
        /// <returns>The text.</returns>
        public static string Ms(double value)
        {
            if (value >= 1000) return (value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "s";
            return value.ToString("0", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats a throughput in tokens per second: "42.5".</summary>
        /// <param name="value">Tokens per second.</param>
        /// <returns>The text.</returns>
        public static string Rate(double value)
        {
            return value.ToString(value >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>Formats a 0-1 ratio as a percentage: "2.5%".</summary>
        /// <param name="ratio">The ratio.</param>
        /// <returns>The text.</returns>
        public static string Percent(double ratio)
        {
            return (ratio * 100.0).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>Formats a duration as a short unit: "1 min", "2 hour", "1 day", "1 week".</summary>
        /// <param name="ms">The duration in milliseconds.</param>
        /// <returns>The text.</returns>
        public static string Interval(long ms)
        {
            long minutes = Math.Max(1, ms / MinuteMs);
            if (minutes < 60) return minutes + " min";
            if (minutes < 1440) return (minutes / 60) + " hour";
            if (minutes % 10080 == 0) return (minutes / 10080) + " week";
            return (minutes / 1440) + " day";
        }

        /// <summary>Formats an offset from a row start: "+0", "+5m", "+1h30", "+2d", "+3w".</summary>
        /// <param name="ms">The offset in milliseconds.</param>
        /// <returns>The text.</returns>
        public static string Offset(long ms)
        {
            long minutes = ms / MinuteMs;
            if (minutes <= 0) return "+0";
            if (minutes < 60) return "+" + minutes + "m";
            if (minutes < 1440) return "+" + (minutes / 60) + "h" + (minutes % 60 == 0 ? string.Empty : (minutes % 60).ToString("00", CultureInfo.InvariantCulture));
            if (minutes % 10080 == 0) return "+" + (minutes / 10080) + "w";
            return "+" + (minutes / 1440) + "d" + (minutes % 1440 == 0 ? string.Empty : (minutes % 1440 / 60) + "h");
        }

        /// <summary>
        /// Formats a bucket window in local time: "11/09 14:00–14:15" for sub-day buckets, "2026-11-09" for a day,
        /// and "2026-11-02–11-08" for longer buckets.
        /// </summary>
        /// <param name="startUnixMs">The bucket start, Unix epoch milliseconds.</param>
        /// <param name="widthMs">The bucket width in milliseconds.</param>
        /// <returns>The text.</returns>
        public static string BucketWindow(long startUnixMs, long widthMs)
        {
            DateTimeOffset start = DateTimeOffset.FromUnixTimeMilliseconds(startUnixMs).ToLocalTime();
            DateTimeOffset end = DateTimeOffset.FromUnixTimeMilliseconds(startUnixMs + Math.Max(1, widthMs)).ToLocalTime();
            if (widthMs < DayMs)
            {
                return start.ToString("MM/dd HH:mm", CultureInfo.InvariantCulture) + "–" + end.ToString("HH:mm", CultureInfo.InvariantCulture);
            }

            if (widthMs == DayMs)
            {
                return start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }

            return start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "–" + end.AddMilliseconds(-1).ToString("MM-dd", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Trims text to a column width, ending it with an ellipsis when it is cut.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="width">The width.</param>
        /// <returns>The trimmed text.</returns>
        public static string Trim(string text, int width)
        {
            if (width <= 0) return string.Empty;
            string value = text ?? string.Empty;
            return value.Length <= width ? value : value.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
