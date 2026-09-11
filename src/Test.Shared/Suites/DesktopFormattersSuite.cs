namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.I18n;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="LocaleFormatters"/>: explicit-culture number, byte, duration,
    /// relative-time, and list formatting, plus null-culture guard behavior.
    /// </summary>
    public static class DesktopFormattersSuite
    {
        /// <summary>
        /// Builds the desktop formatters suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the formatter cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            CultureInfo invariant = CultureInfo.InvariantCulture;
            CultureInfo german = CultureInfo.GetCultureInfo("de-DE");

            return new TestSuiteDescriptor(
                "DesktopFormatters",
                "Locale-aware display formatters",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("DesktopFormatters", "IntegerGrouping", "Integer uses culture grouping", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("1,234", LocaleFormatters.FormatInteger(1234, invariant), "invariant integer");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopFormatters", "NumberCultureAware", "Number honors culture separators", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("1.234,5", LocaleFormatters.FormatNumber(1234.5, german), "german number");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopFormatters", "BytesScaling", "Bytes scale to 1024-based units", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("0 B", LocaleFormatters.FormatBytes(0, invariant), "zero bytes");
                        MuxAssert.AreEqual("1.5 KB", LocaleFormatters.FormatBytes(1536, invariant), "1.5 KB");
                        MuxAssert.AreEqual("-2 KB", LocaleFormatters.FormatBytes(-2048, invariant), "negative bytes");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopFormatters", "DurationBuckets", "Duration renders ms/s/m compactly", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("450 ms", LocaleFormatters.FormatDuration(TimeSpan.FromMilliseconds(450), invariant), "sub-second");
                        MuxAssert.AreEqual("3.5s", LocaleFormatters.FormatDuration(TimeSpan.FromSeconds(3.5), invariant), "seconds");
                        MuxAssert.AreEqual("2m 5s", LocaleFormatters.FormatDuration(TimeSpan.FromSeconds(125), invariant), "minutes");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopFormatters", "RelativeTimeWording", "Relative time picks the right unit", (CancellationToken ct) =>
                    {
                        DateTime now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                        MuxAssert.AreEqual("just now", LocaleFormatters.FormatRelativeTime(now, now, invariant), "just now");
                        MuxAssert.Contains("2 minutes ago", LocaleFormatters.FormatRelativeTime(now.AddMinutes(-2), now, invariant), "minutes ago");
                        MuxAssert.Contains("in 3 hours", LocaleFormatters.FormatRelativeTime(now.AddHours(3), now, invariant), "future hours");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopFormatters", "ListAndPercent", "List joins and percent formats", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("a, b, c", LocaleFormatters.FormatList(new List<string> { "a", "b", "c" }, invariant), "list join");
                        MuxAssert.AreEqual(string.Empty, LocaleFormatters.FormatList(null, invariant), "null list");
                        MuxAssert.Contains("25", LocaleFormatters.FormatPercent(0.25, invariant), "percent");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopFormatters", "NullCultureThrows", "A null culture is rejected", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<ArgumentNullException>(() => LocaleFormatters.FormatInteger(1, null!), "null culture");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
