namespace Mux.Desktop.Views
{
    using System.Collections.Generic;
    using Avalonia.Input;

    /// <summary>
    /// Formats an Avalonia key press into the lowercase <c>modifier+key</c> chord string (for example
    /// <c>ctrl+k</c> or <c>f12</c>) used by <see cref="Mux.Desktop.Services.KeybindingCatalog"/> and persisted
    /// to <c>keybindings.json</c>. Shared by the capture dialog (which records a rebind) and the shell (which
    /// resolves a live key press back to a bound command), so both speak exactly the same chord vocabulary.
    /// </summary>
    internal static class KeyChordFormat
    {
        /// <summary>
        /// Formats a key press as a chord string, or returns null when the press cannot form a chord (a bare
        /// modifier key, or a key with no stable token such as an IME/dead key).
        /// </summary>
        /// <param name="modifiers">The modifiers held during the press.</param>
        /// <param name="key">The pressed key.</param>
        /// <returns>The chord string (for example <c>ctrl+s</c>), or null.</returns>
        public static string? Format(KeyModifiers modifiers, Key key)
        {
            if (IsModifierKey(key))
            {
                return null;
            }

            string? token = KeyToken(key);
            if (token == null)
            {
                return null;
            }

            List<string> parts = new List<string>();
            if ((modifiers & KeyModifiers.Control) != 0)
            {
                parts.Add("ctrl");
            }

            if ((modifiers & KeyModifiers.Alt) != 0)
            {
                parts.Add("alt");
            }

            if ((modifiers & KeyModifiers.Shift) != 0)
            {
                parts.Add("shift");
            }

            parts.Add(token);
            return string.Join("+", parts);
        }

        /// <summary>
        /// Whether the key is a bare modifier (Ctrl/Alt/Shift/Win), which never completes a chord on its own.
        /// </summary>
        /// <param name="key">The key to test.</param>
        /// <returns>True when the key is a modifier.</returns>
        public static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LWin || key == Key.RWin;
        }

        private static string? KeyToken(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return key.ToString().ToLowerInvariant();
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((int)(key - Key.D0)).ToString();
            }

            if (key >= Key.F1 && key <= Key.F24)
            {
                return "f" + (int)(key - Key.F1 + 1);
            }

            switch (key)
            {
                case Key.Enter: return "enter";
                case Key.Space: return "space";
                case Key.Tab: return "tab";
                case Key.Back: return "backspace";
                case Key.Delete: return "delete";
                case Key.Insert: return "insert";
                case Key.Home: return "home";
                case Key.End: return "end";
                case Key.PageUp: return "pageup";
                case Key.PageDown: return "pagedown";
                case Key.Up: return "up";
                case Key.Down: return "down";
                case Key.Left: return "left";
                case Key.Right: return "right";
                default: return null;
            }
        }
    }
}
