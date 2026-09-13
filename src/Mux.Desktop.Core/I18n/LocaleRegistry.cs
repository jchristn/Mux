namespace Mux.Desktop.I18n
{
    using System.Collections.Generic;

    /// <summary>
    /// The set of supported UI locales — the same eleven languages the <c>mux serve</c> dashboard ships, so
    /// the two front ends stay in lockstep. English is the source locale and the default fallback for every
    /// other locale. Every locale ships a complete chrome catalog; only long English-only help text falls
    /// back to English.
    /// </summary>
    public static class LocaleRegistry
    {
        /// <summary>The default (source and fallback) locale code.</summary>
        public static string DefaultLocaleCode
        {
            get => "en";
        }

        /// <summary>
        /// All supported locales, spanning Latin, CJK, Cyrillic, Devanagari, and RTL script families.
        /// </summary>
        public static IReadOnlyList<LocaleInfo> All
        {
            get
            {
                return new List<LocaleInfo>
                {
                    new LocaleInfo("en", "English", "English", false, "en"),
                    new LocaleInfo("es", "Spanish", "Español", false, "en"),
                    new LocaleInfo("pt", "Portuguese", "Português", false, "en"),
                    new LocaleInfo("fr", "French", "Français", false, "en"),
                    new LocaleInfo("it", "Italian", "Italiano", false, "en"),
                    new LocaleInfo("de", "German", "Deutsch", false, "en"),
                    new LocaleInfo("zh", "Mandarin Chinese", "中文", false, "en"),
                    new LocaleInfo("ar", "Arabic", "العربية", true, "en"),
                    new LocaleInfo("ru", "Russian", "Русский", false, "en"),
                    new LocaleInfo("ms", "Malay", "Bahasa Melayu", false, "en"),
                    new LocaleInfo("hi", "Hindi", "हिन्दी", false, "en")
                };
            }
        }
    }
}
