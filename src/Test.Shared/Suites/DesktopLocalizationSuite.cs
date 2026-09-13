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
                    new TestCaseDescriptor("DesktopLocalization", "RegistryAndDefaults", "Eleven locales (dashboard parity); English default resolves a known key", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        MuxAssert.AreEqual(11, service.SupportedLocales.Count, "locale count");
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
                    }),

                    new TestCaseDescriptor("DesktopLocalization", "EveryLocaleResolvesChrome", "Every supported locale resolves the chrome keys to a real (non-key, non-blank) string", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        List<string> keys = new List<string>
                        {
                            StringKeys.NavChat, StringKeys.NavEndpoints, StringKeys.NavMcp, StringKeys.NavSettings,
                            StringKeys.ActSave, StringKeys.ActCancel, StringKeys.ActDelete, StringKeys.ActReload,
                            StringKeys.Conversations, StringKeys.NewConversation, StringKeys.New, StringKeys.Refresh,
                            StringKeys.ComposerPlaceholder, StringKeys.Send, StringKeys.Stop, StringKeys.ChatThinking,
                            StringKeys.ChatYou, StringKeys.ChatAssistant, StringKeys.AboutHelp, StringKeys.License,
                            StringKeys.EmptyStateTitle, StringKeys.AppTagline
                        };

                        foreach (LocaleInfo locale in service.SupportedLocales)
                        {
                            service.SetActiveLocale(locale.Code);
                            foreach (string key in keys)
                            {
                                string value = service.Get(key);
                                MuxAssert.IsFalse(string.IsNullOrEmpty(value), locale.Code + ":" + key + " blank");
                                MuxAssert.AreNotEqual(key, value, locale.Code + ":" + key + " unresolved");
                            }
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopLocalization", "KnownTranslations", "Spot-check real translations from both the dashboard-shared and desktop-specific catalogs", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();

                        service.SetActiveLocale("es");
                        MuxAssert.AreEqual("Servidores MCP", service.Get(StringKeys.NavMcp), "es nav.mcp");
                        MuxAssert.AreEqual("Nueva conversación", service.Get(StringKeys.NewConversation), "es new conversation");

                        service.SetActiveLocale("fr");
                        MuxAssert.AreEqual("Enregistrer", service.Get(StringKeys.ActSave), "fr act.save");
                        MuxAssert.AreEqual("Envoyer", service.Get(StringKeys.Send), "fr composer.send");

                        service.SetActiveLocale("de");
                        MuxAssert.AreEqual("Stopp", service.Get(StringKeys.Stop), "de composer.stop");

                        service.SetActiveLocale("ar");
                        MuxAssert.AreEqual("يفكر…", service.Get(StringKeys.ChatThinking), "ar chat.thinking");

                        // Long English-only help text resolves to the English value in a non-English locale.
                        service.SetActiveLocale("ru");
                        MuxAssert.IsTrue(service.Get(StringKeys.HelpBody).StartsWith("mux Desktop", StringComparison.Ordinal), "ru help body falls back to english");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
