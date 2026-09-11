namespace Mux.Desktop.I18n
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Resolves user-facing strings for the active locale and exposes the supported-locale registry. All UI
    /// text flows through this service so no localizable literal is embedded in views.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>The active locale code (BCP 47).</summary>
        string CurrentLocale { get; }

        /// <summary>True when the active locale is written right-to-left.</summary>
        bool IsRightToLeft { get; }

        /// <summary>The supported locales for the language selector.</summary>
        IReadOnlyList<LocaleInfo> SupportedLocales { get; }

        /// <summary>Raised after the active locale changes so surfaces can refresh their text and direction.</summary>
        event EventHandler? CultureChanged;

        /// <summary>
        /// Resolve a string for the active locale, falling back to the locale's fallback catalog and then to
        /// the key itself when no translation exists.
        /// </summary>
        /// <param name="key">A stable translation key from <see cref="StringKeys"/>.</param>
        /// <returns>The localized string, or the key when no translation is found.</returns>
        string Get(string key);

        /// <summary>
        /// Set the active locale by code. Unknown codes are ignored. Raises <see cref="CultureChanged"/>
        /// when the locale actually changes.
        /// </summary>
        /// <param name="code">A supported BCP 47 locale code.</param>
        void SetActiveLocale(string code);
    }
}
