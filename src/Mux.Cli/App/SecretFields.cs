namespace Mux.Cli.App
{
    using TUIKit.Input;
    using TUIKit.Widgets;

    /// <summary>
    /// Masked text fields for passwords, API keys, and tokens in the terminal UI forms. A terminal has no eye icon,
    /// so <see cref="RevealKey"/> (Ctrl+R) shows or hides the focused secret, and <see cref="Hint"/> names the key in
    /// the form's footer while a secret has focus.
    /// </summary>
    internal static class SecretFields
    {
        #region Public-Members

        /// <summary>The character drawn for each typed character while a secret is hidden.</summary>
        public const char Mask = '•';

        /// <summary>Toggles a focused secret between hidden and shown, so a typed or pasted value can be checked.</summary>
        public static readonly KeyChord RevealKey = KeyChord.Parse("ctrl+r");

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a masked field that <see cref="RevealKey"/> toggles.
        /// </summary>
        /// <returns>The field.</returns>
        public static TextField Create()
        {
            return new TextField { MaskChar = Mask, RevealKey = RevealKey };
        }

        /// <summary>
        /// The footer hint for the reveal key, for example "Ctrl+R show password", when <paramref name="focused"/>
        /// is a masked field; otherwise null.
        /// </summary>
        /// <param name="focused">The form's focused widget.</param>
        /// <param name="noun">What the field holds: "password", "key", or "secret".</param>
        /// <returns>The hint, or null when focus is not on a secret.</returns>
        public static string? Hint(object? focused, string noun)
        {
            if (focused is not TextField field || !field.IsMasked)
            {
                return null;
            }

            return "Ctrl+R " + (field.IsRevealed ? "hide " : "show ") + noun;
        }

        #endregion
    }
}
