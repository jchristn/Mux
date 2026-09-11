namespace Mux.Desktop.I18n
{
    using System;

    /// <summary>
    /// Immutable metadata for a supported UI locale: its canonical BCP 47 code, display names, text
    /// direction, and fallback locale. Used to populate the language selector and to drive layout
    /// direction when the active locale changes.
    /// </summary>
    public sealed class LocaleInfo
    {
        /// <summary>
        /// Instantiate locale metadata.
        /// </summary>
        /// <param name="code">Canonical BCP 47 locale code (for example <c>en</c> or <c>pt-BR</c>). Required.</param>
        /// <param name="englishName">The locale's name in English (for example <c>Portuguese</c>). Required.</param>
        /// <param name="nativeName">The locale's autonym (for example <c>Português</c>). Required.</param>
        /// <param name="isRightToLeft">True when the locale is written right-to-left (for example Arabic).</param>
        /// <param name="fallbackCode">Locale code to fall back to for missing keys. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public LocaleInfo(string code, string englishName, string nativeName, bool isRightToLeft, string fallbackCode)
        {
            ArgumentNullException.ThrowIfNull(code);
            ArgumentNullException.ThrowIfNull(englishName);
            ArgumentNullException.ThrowIfNull(nativeName);
            ArgumentNullException.ThrowIfNull(fallbackCode);

            _Code = code;
            _EnglishName = englishName;
            _NativeName = nativeName;
            _IsRightToLeft = isRightToLeft;
            _FallbackCode = fallbackCode;
        }

        private readonly string _Code;
        private readonly string _EnglishName;
        private readonly string _NativeName;
        private readonly bool _IsRightToLeft;
        private readonly string _FallbackCode;

        /// <summary>Canonical BCP 47 locale code.</summary>
        public string Code
        {
            get => _Code;
        }

        /// <summary>The locale's name in English.</summary>
        public string EnglishName
        {
            get => _EnglishName;
        }

        /// <summary>The locale's autonym (native display name).</summary>
        public string NativeName
        {
            get => _NativeName;
        }

        /// <summary>True when the locale is written right-to-left.</summary>
        public bool IsRightToLeft
        {
            get => _IsRightToLeft;
        }

        /// <summary>Locale code used to resolve keys missing from this locale's catalog.</summary>
        public string FallbackCode
        {
            get => _FallbackCode;
        }
    }
}
