namespace Mux.Cli.App
{
    using System;
    using Mux.Core.Theming;
    using TUIKit;
    using TUIKit.Theming;

    /// <summary>
    /// Maps a shared <see cref="ColorPalette"/> onto the terminal: a TUIKit <see cref="Theme"/> for the widgets,
    /// the input-surface style for the prompt rectangle, a frame pass that paints cells left at the terminal's
    /// default colors with the palette background and text, and the themed, blinking caret.
    /// </summary>
    internal static class TuiPalette
    {
        #region Public-Members

        /// <summary>
        /// How long the caret stays in each blink phase, in milliseconds (the conventional ~1 Hz blink).
        /// </summary>
        public const int BlinkPhaseMilliseconds = 530;

        /// <summary>
        /// The mux default's input surface (the lighter charcoal the shell has always used).
        /// </summary>
        public static readonly Color MuxInputBackground = Color.FromRgb(0x3A, 0x3A, 0x3A);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Converts a palette color to a terminal color.
        /// </summary>
        /// <param name="hex">The <c>#rrggbb</c> color.</param>
        /// <param name="fallback">Returned when the color does not parse.</param>
        /// <returns>The terminal color.</returns>
        public static Color ToColor(string? hex, Color fallback)
        {
            return ColorPaletteCatalog.TryParseRgb(hex, out byte r, out byte g, out byte b) ? Color.FromRgb(r, g, b) : fallback;
        }

        /// <summary>
        /// Builds a painted TUIKit theme from a palette: text, accent, border, and muted over the palette
        /// background, status colors, selection, and the conventional roles (sidebar, status bar, focus border,
        /// markdown, toast, focused button, search match).
        /// </summary>
        /// <param name="palette">A resolved palette (every color set).</param>
        /// <returns>The theme, named after the palette id.</returns>
        public static Theme BuildTheme(ColorPalette palette)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            ColorPaletteColors c = palette.Colors;
            Color background = ToColor(c.Background, Color.Default);
            Color surface = ToColor(c.Surface, background);
            Color surfaceAlt = ToColor(c.SurfaceAlt, surface);
            Color textColor = ToColor(c.Text, Color.Default);
            Color accentColor = ToColor(c.Accent, textColor);

            CellStyle text = new CellStyle(textColor, background);
            CellStyle accent = new CellStyle(accentColor, background);
            CellStyle border = new CellStyle(ToColor(c.Border, textColor), background);
            CellStyle muted = new CellStyle(ToColor(c.Muted, textColor), background);
            bool ascii = string.Equals(palette.Id, ColorPaletteCatalog.HighContrastId, StringComparison.Ordinal);

            Theme theme = new Theme(
                palette.Id,
                text,
                accent,
                border,
                muted,
                ascii,
                success: new CellStyle(ToColor(c.Success, accentColor), background),
                warning: new CellStyle(ToColor(c.Warning, accentColor), background),
                error: new CellStyle(ToColor(c.Error, accentColor), background),
                info: new CellStyle(ToColor(c.Info, accentColor), background),
                selection: new CellStyle(textColor, ToColor(c.Selection, surfaceAlt)));

            CellStyle focus = accent.WithAttribute(CellAttributes.Bold, true);
            theme.SetStyle(Theme.SidebarRole, text.WithBackground(surface));
            theme.SetStyle(Theme.StatusBarRole, text.WithBackground(surfaceAlt));
            theme.SetStyle(Theme.FocusBorderRole, focus);
            theme.SetStyle(Theme.FocusTitleRole, focus);
            theme.SetStyle(Theme.TabFocusedRole, theme.Selection.WithAttribute(CellAttributes.Bold, true).WithAttribute(CellAttributes.Underline, true));
            theme.SetStyle(Theme.InlineButtonHoverRole, accent.WithAttribute(CellAttributes.Underline, true));
            theme.SetStyle(Theme.ButtonFocusedRole, new CellStyle(ToColor(c.AccentText, background), accentColor, CellAttributes.Bold));
            theme.SetStyle(Theme.ToastRole, text.WithBackground(surfaceAlt));
            theme.SetStyle(Theme.MarkdownHeadingRole, focus);
            theme.SetStyle(Theme.MarkdownSubheadingRole, accent);
            theme.SetStyle(Theme.MarkdownCodeRole, text.WithBackground(surfaceAlt));
            theme.SetStyle(Theme.MarkdownInlineCodeRole, accent.WithBackground(surfaceAlt));
            theme.SetStyle(Theme.MarkdownQuoteRole, muted.WithAttribute(CellAttributes.Italic, true));
            theme.SetStyle(Theme.SearchMatchRole, new CellStyle(background, ToColor(c.Warning, accentColor)));
            return theme;
        }

        /// <summary>
        /// The prompt-rectangle style: palette text on the raised surface. The mux default keeps the shell's
        /// light-grey on charcoal.
        /// </summary>
        /// <param name="palette">The palette, or null for the mux default.</param>
        /// <returns>The input surface style.</returns>
        public static CellStyle InputSurfaceFor(ColorPalette? palette)
        {
            if (palette == null)
            {
                return CellStyle.Default.WithForeground(Color.FromPalette(7)).WithBackground(MuxInputBackground);
            }

            Color background = ToColor(palette.Colors.SurfaceAlt, MuxInputBackground);
            return new CellStyle(ToColor(palette.Colors.Text, Color.FromPalette(7)), background);
        }

        /// <summary>
        /// A background one step away from <paramref name="background"/> in the blue channel: visually the same
        /// color, but distinguishable in the frame buffer so the overlay can find the live prompt row.
        /// </summary>
        /// <param name="background">The input-surface background.</param>
        /// <returns>The marker color.</returns>
        public static Color MarkerFor(Color background)
        {
            if (!TryGetRgb(background, out byte r, out byte g, out byte b))
            {
                return Color.FromRgb(0x3A, 0x3A, 0x3B);
            }

            return Color.FromRgb(r, g, b == 255 ? (byte)254 : (byte)(b + 1));
        }

        /// <summary>
        /// Paints every cell still at the terminal's default background with the palette background (and a
        /// default foreground with the palette text color), so gutters and unthemed text conform to a painted
        /// palette. Cells that already carry a color are left alone.
        /// </summary>
        /// <param name="surface">The composed frame.</param>
        /// <param name="background">The palette background.</param>
        /// <param name="text">The palette text color.</param>
        public static void PaintDefaults(BufferSurface surface, Color background, Color text)
        {
            if (surface == null) return;
            int width = surface.Size.Width;
            int height = surface.Size.Height;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Cell cell = surface.Get(x, y);
                    if (cell.Style.Background != Color.Default)
                    {
                        continue;
                    }

                    CellStyle style = cell.Style.WithBackground(background);
                    if (style.Foreground == Color.Default)
                    {
                        style = style.WithForeground(text);
                    }

                    surface.Set(x, y, Restyle(cell, style));
                }
            }
        }

        /// <summary>
        /// Finds the editor's reverse-video caret cell inside <paramref name="bounds"/> and redraws it in the
        /// palette's cursor color, or as plain text during the blink's off phase.
        /// </summary>
        /// <param name="surface">The composed frame.</param>
        /// <param name="bounds">The composer's content rectangle.</param>
        /// <param name="cursor">The caret color.</param>
        /// <param name="cursorText">The glyph color drawn on the caret.</param>
        /// <param name="visible">False during the blink's off phase.</param>
        /// <returns>True when a caret cell was found.</returns>
        public static bool RestyleCaret(BufferSurface surface, Rect bounds, Color cursor, Color cursorText, bool visible)
        {
            if (surface == null) return false;
            int right = Math.Min(surface.Size.Width, bounds.X + bounds.Width);
            int bottom = Math.Min(surface.Size.Height, bounds.Y + bounds.Height);
            for (int y = Math.Max(0, bounds.Y); y < bottom; y++)
            {
                for (int x = Math.Max(0, bounds.X); x < right; x++)
                {
                    Cell cell = surface.Get(x, y);
                    if (cell.IsContinuation || !cell.Style.HasAttribute(CellAttributes.Reverse))
                    {
                        continue;
                    }

                    // The editor draws the caret as its normal style reversed; undo the swap, then either paint the
                    // cursor color behind the glyph or leave the glyph as ordinary text for the off phase.
                    CellStyle plain = cell.Style.WithAttribute(CellAttributes.Reverse, false);
                    CellStyle style = visible ? new CellStyle(cursorText, cursor, plain.Attributes) : plain;
                    surface.Set(x, y, Restyle(cell, style));
                    if (cell.Width == 2 && x + 1 < right)
                    {
                        surface.Set(x + 1, y, Cell.Continuation(style));
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the caret is in its visible phase: always while typing (for one phase after the last
        /// keystroke) or when blinking is off, otherwise alternating every <see cref="BlinkPhaseMilliseconds"/>.
        /// </summary>
        /// <param name="nowMilliseconds">The current clock.</param>
        /// <param name="lastInputMilliseconds">When the composer last received input.</param>
        /// <param name="blink">Whether blinking is enabled.</param>
        /// <returns>True when the caret should be drawn.</returns>
        public static bool CaretVisible(long nowMilliseconds, long lastInputMilliseconds, bool blink)
        {
            if (!blink)
            {
                return true;
            }

            long sinceInput = nowMilliseconds - lastInputMilliseconds;
            if (sinceInput < BlinkPhaseMilliseconds)
            {
                return true;
            }

            return ((sinceInput / BlinkPhaseMilliseconds) % 2) == 0;
        }

        #endregion

        #region Private-Methods

        private static Cell Restyle(Cell cell, CellStyle style)
        {
            if (cell.IsContinuation)
            {
                return Cell.Continuation(style);
            }

            return string.IsNullOrEmpty(cell.Grapheme) ? Cell.Blank(style) : Cell.Glyph(cell.Grapheme, style, Math.Max(1, cell.Width));
        }

        private static bool TryGetRgb(Color color, out byte r, out byte g, out byte b)
        {
            r = color.R;
            g = color.G;
            b = color.B;
            return color.Kind == ColorKind.Rgb;
        }

        #endregion
    }
}
