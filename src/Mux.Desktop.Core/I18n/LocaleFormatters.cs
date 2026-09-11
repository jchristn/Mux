namespace Mux.Desktop.I18n
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// Locale-aware display formatters. Every method takes an explicit <see cref="CultureInfo"/> so display
    /// formatting never depends on an implicit ambient culture, per the project internationalization
    /// requirements. Numbers, dates, durations, byte sizes, and percentages route through the culture's
    /// conventions; relative-time and list joining use a simple baseline that later phases replace with
    /// catalog-backed pluralization and an ICU-style list formatter.
    /// </summary>
    public static class LocaleFormatters
    {
        private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>
        /// Format an integer with the culture's group separators.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <param name="culture">The culture whose number conventions apply. Required.</param>
        /// <returns>The formatted integer.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatInteger(long value, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            return value.ToString("#,##0", culture);
        }

        /// <summary>
        /// Format a number with the culture's group and decimal separators, keeping up to three decimals.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <param name="culture">The culture whose number conventions apply. Required.</param>
        /// <returns>The formatted number.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatNumber(double value, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            return value.ToString("#,##0.###", culture);
        }

        /// <summary>
        /// Format a date using the culture's short-date pattern.
        /// </summary>
        /// <param name="value">The date to format.</param>
        /// <param name="culture">The culture whose date conventions apply. Required.</param>
        /// <returns>The formatted date.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatDate(DateTime value, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            return value.ToString("d", culture);
        }

        /// <summary>
        /// Format a time using the culture's short-time pattern.
        /// </summary>
        /// <param name="value">The time to format.</param>
        /// <param name="culture">The culture whose time conventions apply. Required.</param>
        /// <returns>The formatted time.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatTime(DateTime value, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            return value.ToString("t", culture);
        }

        /// <summary>
        /// Format a date and time using the culture's general (short date + short time) pattern.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <param name="culture">The culture whose conventions apply. Required.</param>
        /// <returns>The formatted date-time.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatDateTime(DateTime value, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            return value.ToString("g", culture);
        }

        /// <summary>
        /// Format a fraction (0.0–1.0) as a percentage with one decimal, using the culture's percent format.
        /// </summary>
        /// <param name="fraction">The fraction to format; 0.25 renders as 25%.</param>
        /// <param name="culture">The culture whose percent conventions apply. Required.</param>
        /// <returns>The formatted percentage.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatPercent(double fraction, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            return fraction.ToString("P1", culture);
        }

        /// <summary>
        /// Format a byte count as a human-readable size (B, KB, MB, …) with the culture's number format.
        /// Uses 1024-based units.
        /// </summary>
        /// <param name="bytes">The number of bytes. Negative values are formatted with a leading sign.</param>
        /// <param name="culture">The culture whose number conventions apply. Required.</param>
        /// <returns>The formatted size, for example <c>1.5 MB</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatBytes(long bytes, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);

            if (bytes == 0)
            {
                return "0 " + ByteUnits[0];
            }

            bool negative = bytes < 0;
            double magnitude = negative ? -(double)bytes : bytes;
            int unit = 0;

            while (magnitude >= 1024.0 && unit < ByteUnits.Length - 1)
            {
                magnitude /= 1024.0;
                unit++;
            }

            string number = unit == 0
                ? magnitude.ToString("#,##0", culture)
                : magnitude.ToString("#,##0.#", culture);

            return (negative ? "-" : string.Empty) + number + " " + ByteUnits[unit];
        }

        /// <summary>
        /// Format a duration as a compact human-readable string (for example <c>1h 2m</c>, <c>3.5s</c>, or
        /// <c>450 ms</c>), using the culture's number format for fractional seconds.
        /// </summary>
        /// <param name="duration">The duration to format. Negative durations are treated as their magnitude.</param>
        /// <param name="culture">The culture whose number conventions apply. Required.</param>
        /// <returns>The formatted duration.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatDuration(TimeSpan duration, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);

            TimeSpan span = duration < TimeSpan.Zero ? duration.Negate() : duration;

            if (span.TotalMilliseconds < 1000.0)
            {
                return Math.Round(span.TotalMilliseconds).ToString("#,##0", culture) + " ms";
            }

            if (span.TotalSeconds < 60.0)
            {
                return span.TotalSeconds.ToString("#,##0.#", culture) + "s";
            }

            if (span.TotalMinutes < 60.0)
            {
                return span.Minutes.ToString(culture) + "m " + span.Seconds.ToString(culture) + "s";
            }

            if (span.TotalHours < 24.0)
            {
                return span.Hours.ToString(culture) + "h " + span.Minutes.ToString(culture) + "m";
            }

            return ((int)span.TotalDays).ToString(culture) + "d " + span.Hours.ToString(culture) + "h";
        }

        /// <summary>
        /// Format a coarse relative time relative to <paramref name="nowUtc"/> (for example <c>just now</c>,
        /// <c>5 minutes ago</c>, or <c>in 2 hours</c>). Baseline English wording; later phases route the unit
        /// labels through the translation catalog with plural rules.
        /// </summary>
        /// <param name="value">The instant being described.</param>
        /// <param name="nowUtc">The reference "now".</param>
        /// <param name="culture">The culture whose number conventions apply. Required.</param>
        /// <returns>A relative-time phrase.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatRelativeTime(DateTime value, DateTime nowUtc, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);

            TimeSpan delta = nowUtc - value;
            bool past = delta >= TimeSpan.Zero;
            TimeSpan magnitude = past ? delta : delta.Negate();

            if (magnitude.TotalSeconds < 45.0)
            {
                return "just now";
            }

            long amount;
            string unit;

            if (magnitude.TotalMinutes < 60.0)
            {
                amount = Math.Max(1, (long)Math.Round(magnitude.TotalMinutes));
                unit = amount == 1 ? "minute" : "minutes";
            }
            else if (magnitude.TotalHours < 24.0)
            {
                amount = Math.Max(1, (long)Math.Round(magnitude.TotalHours));
                unit = amount == 1 ? "hour" : "hours";
            }
            else if (magnitude.TotalDays < 30.0)
            {
                amount = Math.Max(1, (long)Math.Round(magnitude.TotalDays));
                unit = amount == 1 ? "day" : "days";
            }
            else if (magnitude.TotalDays < 365.0)
            {
                amount = Math.Max(1, (long)Math.Round(magnitude.TotalDays / 30.0));
                unit = amount == 1 ? "month" : "months";
            }
            else
            {
                amount = Math.Max(1, (long)Math.Round(magnitude.TotalDays / 365.0));
                unit = amount == 1 ? "year" : "years";
            }

            string number = amount.ToString(culture);
            return past ? number + " " + unit + " ago" : "in " + number + " " + unit;
        }

        /// <summary>
        /// Join a list of items for display. Baseline uses a comma-space separator; later phases replace this
        /// with an ICU-style list formatter (for example "a, b, and c").
        /// </summary>
        /// <param name="items">The items to join. Null or empty yields an empty string.</param>
        /// <param name="culture">The culture whose list conventions apply. Required.</param>
        /// <returns>The joined string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="culture"/> is null.</exception>
        public static string FormatList(IReadOnlyList<string>? items, CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);

            if (items == null || items.Count == 0)
            {
                return string.Empty;
            }

            string separator = culture.TextInfo.ListSeparator;
            if (string.IsNullOrEmpty(separator))
            {
                separator = ",";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(separator).Append(' ');
                }

                builder.Append(items[i]);
            }

            return builder.ToString();
        }
    }
}
