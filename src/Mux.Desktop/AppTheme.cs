namespace Mux.Desktop
{
    using System;
    using Avalonia.Media;
    using Mux.Core.Theming;

    /// <summary>
    /// The application color palette: slate neutrals (shared with the Armada dashboard and Armada Harbor) with
    /// the mux green accent, offered in light, dark, and high-contrast variants. A single <see cref="Current"/>
    /// instance is read by the windows at build time, and <see cref="Styling.MuxThemeStyles"/> mirrors it into
    /// the Fluent control resources. <see cref="Toggle"/>, <see cref="Set"/>, and <see cref="SetHighContrast"/>
    /// swap it and raise <see cref="Changed"/>; the shell rebuilds against the new palette.
    /// Named <c>AppTheme</c> to avoid colliding with Avalonia's <c>StyledElement.Theme</c> property.
    /// </summary>
    public sealed class AppTheme
    {
        /// <summary>
        /// Raised after <see cref="Current"/> changes, on the thread that changed it.
        /// </summary>
        public static event EventHandler? Changed;

        /// <summary>Whether this is the dark variant.</summary>
        public bool IsDark { get; private set; }

        /// <summary>Whether this is the high-contrast (accessibility) variant.</summary>
        public bool IsHighContrast { get; private set; }

        /// <summary>Primary window/background surface.</summary>
        public IBrush Surface { get; private set; } = Brushes.White;

        /// <summary>Secondary surface (sidebar, bubbles, cards).</summary>
        public IBrush SurfaceAlt { get; private set; } = Brushes.White;

        /// <summary>Primary text.</summary>
        public IBrush Text { get; private set; } = Brushes.Black;

        /// <summary>Muted/secondary text.</summary>
        public IBrush Muted { get; private set; } = Brushes.Gray;

        /// <summary>Hover fill for rows, quiet buttons, and list items.</summary>
        public IBrush Hover { get; private set; } = Brushes.WhiteSmoke;

        /// <summary>Border of text inputs, combo boxes, and secondary buttons (a step stronger than <see cref="Border"/>).</summary>
        public IBrush InputBorder { get; private set; } = Brushes.LightGray;

        /// <summary>A soft tint of the accent, for selected rows and badges.</summary>
        public IBrush AccentSoft { get; private set; } = Brushes.Honeydew;

        /// <summary>Subtle border/separator.</summary>
        public IBrush Border { get; private set; } = Brushes.LightGray;

        /// <summary>The accent color (mux green), used for links, inline code, and highlights.</summary>
        public IBrush Accent { get; private set; } = Brushes.Green;

        /// <summary>The background for primary buttons (a stronger green than <see cref="Accent"/>).</summary>
        public IBrush AccentButton { get; private set; } = Brushes.Green;

        /// <summary>Text drawn on primary buttons (white).</summary>
        public IBrush AccentText { get; private set; } = Brushes.White;

        /// <summary>User message bubble background.</summary>
        public IBrush UserBubble { get; private set; } = Brushes.WhiteSmoke;

        /// <summary>Assistant message bubble background.</summary>
        public IBrush AssistantBubble { get; private set; } = Brushes.White;

        /// <summary>Error/danger color.</summary>
        public IBrush Error { get; private set; } = Brushes.Red;

        /// <summary>Success color.</summary>
        public IBrush Success { get; private set; } = Brushes.Green;

        /// <summary>The active palette. Defaults to the dark (TUI-anchored) variant.</summary>
        /// <summary>
        /// The text caret color (the palette's <c>cursor</c> role).
        /// </summary>
        public IBrush Cursor { get; private set; } = Brushes.Green;

        /// <summary>
        /// The background of selected text and rows.
        /// </summary>
        public IBrush Selection { get; private set; } = Brushes.LightGreen;

        /// <summary>
        /// Warning states.
        /// </summary>
        public IBrush Warning { get; private set; } = Brushes.Orange;

        /// <summary>
        /// Informational states.
        /// </summary>
        public IBrush Info { get; private set; } = Brushes.SteelBlue;

        /// <summary>
        /// The id of the palette this theme was built from (for example <c>mux</c> or <c>midnight</c>).
        /// </summary>
        public string PaletteId { get; private set; } = ColorPaletteCatalog.DefaultId;

        /// <summary>
        /// Gets the active palette.
        /// </summary>
        public static AppTheme Current { get; private set; } = FromPalette(ColorPaletteCatalog.Resolve(ColorPaletteCatalog.DefaultId, null));

        /// <summary>
        /// Makes a palette the active theme and raises <see cref="Changed"/>.
        /// </summary>
        /// <param name="palette">A resolved palette.</param>
        /// <returns>The new current theme.</returns>
        public static AppTheme SetPalette(ColorPalette palette)
        {
            Current = FromPalette(palette);
            Changed?.Invoke(null, EventArgs.Empty);
            return Current;
        }

        /// <summary>
        /// Builds a theme from a shared palette. The Mux, Mux Light, and High Contrast palettes keep the desktop
        /// app's hand-tuned brushes (so the default look is unchanged); every other palette maps its roles
        /// directly and derives the secondary shades (hover, soft accent, input border) by blending.
        /// </summary>
        /// <param name="palette">A resolved palette (every color set).</param>
        /// <returns>The theme.</returns>
        public static AppTheme FromPalette(ColorPalette palette)
        {
            if (palette == null) throw new ArgumentNullException(nameof(palette));
            ColorPaletteColors c = palette.Colors;
            AppTheme theme;
            switch (palette.Id)
            {
                case ColorPaletteCatalog.DefaultId:
                    theme = CreateDark();
                    break;
                case ColorPaletteCatalog.LightId:
                    theme = CreateLight();
                    break;
                case ColorPaletteCatalog.HighContrastId:
                    theme = CreateHighContrast();
                    break;
                default:
                    string background = c.Background ?? "#000000";
                    theme = new AppTheme
                    {
                        IsDark = palette.Dark,
                        Surface = Solid(background),
                        SurfaceAlt = Solid(c.Surface ?? background),
                        Text = Solid(c.Text ?? "#ffffff"),
                        Muted = Solid(c.Muted ?? "#888888"),
                        Border = Solid(c.Border ?? "#444444"),
                        Hover = Solid(c.SurfaceAlt ?? background),
                        InputBorder = Solid(ColorPaletteCatalog.Mix(c.Border ?? "#444444", c.Text ?? "#ffffff", 0.22)),
                        Accent = Solid(c.Accent ?? "#4ade80"),
                        AccentSoft = Solid(ColorPaletteCatalog.Mix(background, c.Accent ?? "#4ade80", palette.Dark ? 0.2 : 0.16)),
                        AccentButton = Solid(c.Accent ?? "#4ade80"),
                        AccentText = Solid(c.AccentText ?? "#000000"),
                        UserBubble = Solid(c.SurfaceAlt ?? background),
                        AssistantBubble = Solid(c.Surface ?? background),
                        Error = Solid(c.Error ?? "#ff5555"),
                        Success = Solid(c.Success ?? "#55ff55")
                    };
                    break;
            }

            theme.PaletteId = palette.Id;
            theme.Cursor = Solid(c.Cursor ?? c.Accent ?? "#4ade80");
            theme.Selection = Solid(c.Selection ?? c.SurfaceAlt ?? "#335533");
            theme.Warning = Solid(c.Warning ?? "#ffaa00");
            theme.Info = Solid(c.Info ?? "#55aaff");
            return theme;
        }

        /// <summary>
        /// Switches between the Mux and Mux Light palettes.
        /// </summary>
        /// <returns>The new current theme.</returns>
        public static AppTheme Toggle()
        {
            return Set(!Current.IsDark);
        }

        /// <summary>
        /// Applies the Mux (dark) or Mux Light palette.
        /// </summary>
        /// <param name="dark">True for dark.</param>
        /// <returns>The new current theme.</returns>
        public static AppTheme Set(bool dark)
        {
            return SetPalette(ColorPaletteCatalog.Resolve(dark ? ColorPaletteCatalog.DefaultId : ColorPaletteCatalog.LightId, null));
        }

        /// <summary>
        /// Applies the High Contrast palette.
        /// </summary>
        /// <returns>The new current theme.</returns>
        public static AppTheme SetHighContrast()
        {
            return SetPalette(ColorPaletteCatalog.Resolve(ColorPaletteCatalog.HighContrastId, null));
        }

        /// <summary>
        /// Build the high-contrast accessibility palette: pure-black surfaces, pure-white text and borders,
        /// and a bright-yellow accent, for maximum contrast (WCAG-friendly).
        /// </summary>
        /// <returns>The high-contrast palette.</returns>
        public static AppTheme CreateHighContrast()
        {
            return new AppTheme
            {
                IsDark = true,
                IsHighContrast = true,
                Surface = Solid("#000000"),
                SurfaceAlt = Solid("#0a0a0a"),
                Text = Solid("#ffffff"),
                Muted = Solid("#e6e6e6"),
                Border = Solid("#ffffff"),
                Hover = Solid("#1f1f1f"),
                InputBorder = Solid("#ffffff"),
                Accent = Solid("#ffff00"),
                AccentSoft = Solid("#333300"),
                AccentButton = Solid("#ffff00"),
                AccentText = Solid("#000000"),
                UserBubble = Solid("#1a1a1a"),
                AssistantBubble = Solid("#000000"),
                Error = Solid("#ff8080"),
                Success = Solid("#80ff80")
            };
        }

        /// <summary>
        /// Build the dark (TUI-anchored) palette: dark terminal surfaces, light text, mux green accent.
        /// </summary>
        /// <returns>The dark palette.</returns>
        public static AppTheme CreateDark()
        {
            // Slate dark (Armada dashboard): page #080d1a, card #111827, hover #1e293b, text #f1f5f9.
            return new AppTheme
            {
                IsDark = true,
                Surface = Solid("#080d1a"),
                SurfaceAlt = Solid("#111827"),
                Text = Solid("#f1f5f9"),
                Muted = Solid("#94a3b8"),
                Border = Solid("#1e293b"),
                Hover = Solid("#1e293b"),
                InputBorder = Solid("#334155"),
                Accent = Solid("#4ade80"),
                AccentSoft = Solid("#052e16"),
                AccentButton = Solid("#16a34a"),
                AccentText = Solid("#ffffff"),
                UserBubble = Solid("#172033"),
                AssistantBubble = Solid("#111827"),
                Error = Solid("#f87171"),
                Success = Solid("#22c55e")
            };
        }

        /// <summary>
        /// Build the light palette: white surfaces, dark text, mux green accent.
        /// </summary>
        /// <returns>The light palette.</returns>
        public static AppTheme CreateLight()
        {
            // Slate light (Armada dashboard): page #f8fafc, card #ffffff, hover #f1f5f9, text #1e293b.
            return new AppTheme
            {
                IsDark = false,
                Surface = Solid("#f8fafc"),
                SurfaceAlt = Solid("#ffffff"),
                Text = Solid("#1e293b"),
                Muted = Solid("#5b6b80"),
                Border = Solid("#e2e8f0"),
                Hover = Solid("#f1f5f9"),
                InputBorder = Solid("#cbd5e1"),
                Accent = Solid("#15803d"),
                AccentSoft = Solid("#dcfce7"),
                AccentButton = Solid("#16a34a"),
                AccentText = Solid("#ffffff"),
                UserBubble = Solid("#eef2f7"),
                AssistantBubble = Solid("#ffffff"),
                Error = Solid("#c81e1e"),
                Success = Solid("#15803d")
            };
        }

        private static SolidColorBrush Solid(string hex)
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
    }
}
