namespace Mux.Desktop.I18n
{
    /// <summary>
    /// Ambient access to the active <see cref="ILocalizationService"/> for views that are not handed the
    /// service directly. Set once at application startup (mirroring the <c>AppTheme.Current</c> pattern) so
    /// any window, dialog, or form can resolve a localized string with <see cref="T"/> without plumbing the
    /// service through every constructor. Backed by the same singleton the shell uses, so a locale change made
    /// through the shell is reflected everywhere the next time a string is resolved.
    /// </summary>
    public static class Localizer
    {
        private static ILocalizationService? _Current;

        /// <summary>
        /// The active localization service. Assigned once at startup. Until then, <see cref="T"/> echoes the
        /// key so the app still renders (with keys visible) rather than throwing.
        /// </summary>
        public static ILocalizationService? Current
        {
            get => _Current;
            set => _Current = value;
        }

        /// <summary>
        /// Resolve a localized string for the active locale. Returns the key itself when no service is set or
        /// the key is unknown, so a missing translation is visible rather than blank.
        /// </summary>
        /// <param name="key">The translation key (see <see cref="StringKeys"/> or the generated catalog).</param>
        /// <returns>The localized string, or the key when unresolved.</returns>
        public static string T(string key)
        {
            ILocalizationService? service = _Current;
            return service != null ? service.Get(key) : (key ?? string.Empty);
        }
    }
}
