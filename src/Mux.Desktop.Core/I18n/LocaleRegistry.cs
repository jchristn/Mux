namespace Mux.Desktop.I18n
{
    using System.Collections.Generic;

    /// <summary>
    /// The baseline set of supported UI locales, per the project internationalization requirements. English
    /// is the source locale and the default fallback for every other locale. Catalogs for locales other than
    /// English are populated in later phases; until then they resolve through the English fallback.
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
                    new LocaleInfo("hi", "Hindi", "हिन्दी", false, "en"),
                    new LocaleInfo("ja", "Japanese", "日本語", false, "en")
                };
            }
        }
    }
}
