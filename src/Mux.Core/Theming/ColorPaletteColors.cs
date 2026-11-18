namespace Mux.Core.Theming
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The fourteen color roles a palette defines, as <c>#rrggbb</c> hex strings. Every surface maps these
    /// roles onto its own styling: the TUI builds a terminal theme, the desktop app a brush set, and the web
    /// dashboard and VS Code webviews CSS custom properties. A custom palette may leave any role null to
    /// inherit it from its base palette (see <see cref="ColorPalette.Base"/>).
    /// </summary>
    public sealed class ColorPaletteColors
    {
        #region Public-Members

        /// <summary>
        /// The names of every role, in display order, matching the JSON property names.
        /// </summary>
        public static readonly IReadOnlyList<string> RoleNames = new[]
        {
            "background", "surface", "surfaceAlt", "border", "text", "muted", "accent", "accentText",
            "cursor", "selection", "success", "warning", "error", "info"
        };

        /// <summary>
        /// The page or window background.
        /// </summary>
        [JsonPropertyName("background")]
        public string? Background { get; set; }

        /// <summary>
        /// Panels, cards, and sidebars that sit on the background.
        /// </summary>
        [JsonPropertyName("surface")]
        public string? Surface { get; set; }

        /// <summary>
        /// Raised areas: inputs, the prompt composer, hover rows, and user message bubbles.
        /// </summary>
        [JsonPropertyName("surfaceAlt")]
        public string? SurfaceAlt { get; set; }

        /// <summary>
        /// Dividers, outlines, and input borders.
        /// </summary>
        [JsonPropertyName("border")]
        public string? Border { get; set; }

        /// <summary>
        /// Primary text.
        /// </summary>
        [JsonPropertyName("text")]
        public string? Text { get; set; }

        /// <summary>
        /// Secondary text: hints, captions, timestamps.
        /// </summary>
        [JsonPropertyName("muted")]
        public string? Muted { get; set; }

        /// <summary>
        /// The brand or highlight color: links, active items, primary buttons, the prompt marker.
        /// </summary>
        [JsonPropertyName("accent")]
        public string? Accent { get; set; }

        /// <summary>
        /// Text drawn on top of <see cref="Accent"/> (primary buttons, active navigation).
        /// </summary>
        [JsonPropertyName("accentText")]
        public string? AccentText { get; set; }

        /// <summary>
        /// The text caret in prompt and form inputs.
        /// </summary>
        [JsonPropertyName("cursor")]
        public string? Cursor { get; set; }

        /// <summary>
        /// The background of selected rows and selected text.
        /// </summary>
        [JsonPropertyName("selection")]
        public string? Selection { get; set; }

        /// <summary>
        /// Success states.
        /// </summary>
        [JsonPropertyName("success")]
        public string? Success { get; set; }

        /// <summary>
        /// Warning states.
        /// </summary>
        [JsonPropertyName("warning")]
        public string? Warning { get; set; }

        /// <summary>
        /// Errors and destructive actions.
        /// </summary>
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        /// <summary>
        /// Informational states.
        /// </summary>
        [JsonPropertyName("info")]
        public string? Info { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Gets a role's value by its JSON name (case-insensitive).
        /// </summary>
        /// <param name="role">A name from <see cref="RoleNames"/>.</param>
        /// <returns>The hex value, or null when unset.</returns>
        /// <exception cref="ArgumentException">Thrown when the role name is unknown.</exception>
        public string? Get(string role)
        {
            switch ((role ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "background": return Background;
                case "surface": return Surface;
                case "surfacealt": return SurfaceAlt;
                case "border": return Border;
                case "text": return Text;
                case "muted": return Muted;
                case "accent": return Accent;
                case "accenttext": return AccentText;
                case "cursor": return Cursor;
                case "selection": return Selection;
                case "success": return Success;
                case "warning": return Warning;
                case "error": return Error;
                case "info": return Info;
                default: throw new ArgumentException("Unknown palette role '" + role + "'.", nameof(role));
            }
        }

        /// <summary>
        /// Sets a role's value by its JSON name (case-insensitive).
        /// </summary>
        /// <param name="role">A name from <see cref="RoleNames"/>.</param>
        /// <param name="value">The hex value, or null to inherit.</param>
        /// <exception cref="ArgumentException">Thrown when the role name is unknown.</exception>
        public void Set(string role, string? value)
        {
            switch ((role ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "background": Background = value; break;
                case "surface": Surface = value; break;
                case "surfacealt": SurfaceAlt = value; break;
                case "border": Border = value; break;
                case "text": Text = value; break;
                case "muted": Muted = value; break;
                case "accent": Accent = value; break;
                case "accenttext": AccentText = value; break;
                case "cursor": Cursor = value; break;
                case "selection": Selection = value; break;
                case "success": Success = value; break;
                case "warning": Warning = value; break;
                case "error": Error = value; break;
                case "info": Info = value; break;
                default: throw new ArgumentException("Unknown palette role '" + role + "'.", nameof(role));
            }
        }

        /// <summary>
        /// Returns a copy of this color set.
        /// </summary>
        /// <returns>A new instance with the same values.</returns>
        public ColorPaletteColors Clone()
        {
            ColorPaletteColors copy = new ColorPaletteColors();
            foreach (string role in RoleNames)
            {
                copy.Set(role, Get(role));
            }

            return copy;
        }

        #endregion
    }
}
