namespace Mux.Desktop.I18n
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// In-memory localization service. Holds one string catalog per locale and resolves keys against the
    /// active locale, its fallback, and finally the key itself. The English catalog ships complete; catalogs
    /// for other locales are added in later phases and, until then, resolve through the English fallback.
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
            Dictionary<string, IReadOnlyDictionary<string, string>> catalogs =
                new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            Dictionary<string, string> english = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { StringKeys.AppTitle, "mux" },
                { StringKeys.AppTagline, "Your AI agent, your models, your infrastructure." },
                { StringKeys.SplashLoading, "Starting…" },
                { StringKeys.Conversations, "Conversations" },
                { StringKeys.NewConversation, "New conversation" },
                { StringKeys.EmptyStateTitle, "Start a new conversation" },
                { StringKeys.EmptyStateBody, "Your threads will appear here. This is the mux Desktop foundation." },
                { StringKeys.AboutHelp, "About" },
                { StringKeys.HelpHeading, "Getting started" },
                {
                    StringKeys.HelpBody,
                    "mux Desktop runs the mux agent locally against the model and backend you configure. "
                        + "Create a conversation to start a thread, then type a prompt and press Enter. "
                        + "Endpoints, tools, MCP servers, skills, and usage analytics are managed in the app."
                },
                { StringKeys.License, "MIT License" }
            };

            catalogs[LocaleRegistry.DefaultLocaleCode] = english;
            return catalogs;
        }
    }
}
