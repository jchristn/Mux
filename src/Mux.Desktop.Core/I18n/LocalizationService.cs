namespace Mux.Desktop.I18n
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// In-memory localization service. Holds one string catalog per locale and resolves keys against the
    /// active locale, its fallback, and finally the key itself. All supported locales ship complete chrome
    /// catalogs (merged from the dashboard-shared packs and the desktop-specific strings); long English-only
    /// help text resolves through the English fallback by design.
    /// </summary>
    /// <remarks>
    /// Thread safety: intended to be used from the UI thread. <see cref="SetActiveLocale"/> mutates the
    /// active locale and raises <see cref="CultureChanged"/> synchronously.
    /// </remarks>
    public sealed class LocalizationService : ILocalizationService
    {
        private readonly IReadOnlyList<LocaleInfo> _SupportedLocales;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _Catalogs;
        private LocaleInfo _Current;

        /// <summary>
        /// Instantiate the service, seeding the built-in catalogs and defaulting to the source locale.
        /// </summary>
        public LocalizationService()
        {
            _SupportedLocales = LocaleRegistry.All;
            _Catalogs = BuildCatalogs();
            _Current = FindLocale(LocaleRegistry.DefaultLocaleCode) ?? _SupportedLocales[0];
        }

        /// <inheritdoc />
        public event EventHandler? CultureChanged;

        /// <inheritdoc />
        public string CurrentLocale
        {
            get => _Current.Code;
        }

        /// <inheritdoc />
        public bool IsRightToLeft
        {
            get => _Current.IsRightToLeft;
        }

        /// <inheritdoc />
        public IReadOnlyList<LocaleInfo> SupportedLocales
        {
            get => _SupportedLocales;
        }

        /// <inheritdoc />
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            if (TryGetFromCatalog(_Current.Code, key, out string value))
            {
                return value;
            }

            if (!string.Equals(_Current.FallbackCode, _Current.Code, StringComparison.OrdinalIgnoreCase)
                && TryGetFromCatalog(_Current.FallbackCode, key, out string fallbackValue))
            {
                return fallbackValue;
            }

            // Last resort: return the key so a missing translation is visible rather than blank.
            return key;
        }

        /// <inheritdoc />
        public void SetActiveLocale(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            LocaleInfo? match = FindLocale(code);
            if (match == null || string.Equals(match.Code, _Current.Code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _Current = match;

            try
            {
                CultureInfo culture = CultureInfo.GetCultureInfo(match.Code);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
            }
            catch (CultureNotFoundException)
            {
                // Keep the previous culture if the platform does not recognize the code; strings still switch.
            }

            CultureChanged?.Invoke(this, EventArgs.Empty);
        }

        private LocaleInfo? FindLocale(string code)
        {
            foreach (LocaleInfo locale in _SupportedLocales)
            {
                if (string.Equals(locale.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return locale;
                }
            }

            return null;
        }

        private bool TryGetFromCatalog(string localeCode, string key, out string value)
        {
            if (_Catalogs.TryGetValue(localeCode, out IReadOnlyDictionary<string, string>? catalog)
                && catalog.TryGetValue(key, out string? found))
            {
                value = found;
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildCatalogs()
        {
            // Two authored sources merged per locale: the dashboard-shared chrome packs (ported verbatim from
            // mux serve so both front ends share translations) and the desktop-specific strings (splash,
            // sidebar, composer, About). Where both define a key, the desktop-specific value wins.
            Dictionary<string, Dictionary<string, string>> merged =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            MergeInto(merged, DashboardStrings.Build());
            MergeInto(merged, DesktopStrings.Build());

            Dictionary<string, IReadOnlyDictionary<string, string>> catalogs =
                new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in merged)
            {
                catalogs[pair.Key] = pair.Value;
            }

            return catalogs;
        }

        private static void MergeInto(
            Dictionary<string, Dictionary<string, string>> target,
            Dictionary<string, Dictionary<string, string>> source)
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> localePair in source)
            {
                if (!target.TryGetValue(localePair.Key, out Dictionary<string, string>? localeMap))
                {
                    localeMap = new Dictionary<string, string>(StringComparer.Ordinal);
                    target[localePair.Key] = localeMap;
                }

                foreach (KeyValuePair<string, string> entry in localePair.Value)
                {
                    localeMap[entry.Key] = entry.Value;
                }
            }
        }
    }
}
