namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.I18n;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="LocalizationService"/>: catalog resolution, missing-key fallback to
    /// the key, locale switching with the change event, unknown-locale rejection, and RTL metadata.
    /// </summary>
    public static class DesktopLocalizationSuite
    {
        /// <summary>
        /// Builds the desktop localization suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the localization cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "DesktopLocalization",
                "Desktop localization service",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("DesktopLocalization", "RegistryAndDefaults", "Twelve locales; English default resolves a known key", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        MuxAssert.AreEqual(12, service.SupportedLocales.Count, "locale count");
                        MuxAssert.AreEqual("en", service.CurrentLocale, "default locale");
                        MuxAssert.AreEqual("mux", service.Get(StringKeys.AppTitle), "known key");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopLocalization", "MissingKeyReturnsKey", "An unknown key resolves to the key; blank resolves to empty", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        MuxAssert.AreEqual("no.such.key", service.Get("no.such.key"), "missing key");
                        MuxAssert.AreEqual(string.Empty, service.Get(string.Empty), "blank key");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopLocalization", "SwitchLocaleRaisesEvent", "Switching a supported locale changes state and raises the event", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        bool raised = false;
                        service.CultureChanged += (sender, args) => raised = true;

                        service.SetActiveLocale("fr");
                        MuxAssert.AreEqual("fr", service.CurrentLocale, "switched locale");
                        MuxAssert.IsTrue(raised, "culture changed raised");
                        MuxAssert.AreEqual("mux", service.Get(StringKeys.AppTitle), "fallback to english");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopLocalization", "UnknownLocaleIgnored", "An unknown locale code is ignored without an event", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        service.SetActiveLocale("fr");
                        bool raised = false;
                        service.CultureChanged += (sender, args) => raised = true;

                        service.SetActiveLocale("zz");
                        MuxAssert.AreEqual("fr", service.CurrentLocale, "locale unchanged");
                        MuxAssert.IsFalse(raised, "no event for unknown locale");

                        service.SetActiveLocale(null!);
                        MuxAssert.AreEqual("fr", service.CurrentLocale, "null ignored");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopLocalization", "DirectionMetadata", "Arabic is RTL; English is LTR", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        service.SetActiveLocale("ar");
                        MuxAssert.IsTrue(service.IsRightToLeft, "arabic rtl");
                        service.SetActiveLocale("en");
                        MuxAssert.IsFalse(service.IsRightToLeft, "english ltr");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
