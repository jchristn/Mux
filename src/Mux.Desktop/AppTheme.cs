namespace Mux.Desktop
{
    using Avalonia.Media;

    /// <summary>
    /// The application color palette, anchored to the mux TUI's look (dark terminal surfaces with a green
    /// accent) and offered in light and dark variants. A single <see cref="Current"/> instance is read by the
    /// windows at build time; <see cref="Toggle"/> swaps it and the shell rebuilds against the new palette.
    /// Named <c>AppTheme</c> to avoid colliding with Avalonia's <c>StyledElement.Theme</c> property.
    /// </summary>
    public sealed class AppTheme
    {
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
        public static AppTheme Current { get; private set; } = CreateDark();

        /// <summary>
        /// Swap the active palette between light and dark.
        /// </summary>
        /// <returns>The new active palette.</returns>
        public static AppTheme Toggle()
        {
            Current = Current.IsDark ? CreateLight() : CreateDark();
            return Current;
        }

        /// <summary>
        /// Set the active palette to a specific variant.
        /// </summary>
        /// <param name="dark">True for the dark variant; false for light.</param>
        /// <returns>The new active palette.</returns>
        public static AppTheme Set(bool dark)
        {
            Current = dark ? CreateDark() : CreateLight();
            return Current;
        }

        /// <summary>
        /// Set the active palette to the high-contrast accessibility variant.
        /// </summary>
        /// <returns>The new active palette.</returns>
        public static AppTheme SetHighContrast()
        {
            Current = CreateHighContrast();
            return Current;
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
                Accent = Solid("#ffff00"),
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
            return new AppTheme
            {
                IsDark = true,
                Surface = Solid("#0d1117"),
                SurfaceAlt = Solid("#161b22"),
                Text = Solid("#e6edf3"),
                Muted = Solid("#8b949e"),
                Border = Solid("#30363d"),
                Accent = Solid("#3fb950"),
                AccentButton = Solid("#238636"),
                AccentText = Solid("#ffffff"),
                UserBubble = Solid("#1c2333"),
                AssistantBubble = Solid("#161b22"),
                Error = Solid("#f85149"),
                Success = Solid("#3fb950")
            };
        }

        /// <summary>
        /// Build the light palette: white surfaces, dark text, mux green accent.
        /// </summary>
        /// <returns>The light palette.</returns>
        public static AppTheme CreateLight()
        {
            return new AppTheme
            {
                IsDark = false,
                Surface = Solid("#ffffff"),
                SurfaceAlt = Solid("#f6f8fa"),
                Text = Solid("#1f2328"),
                Muted = Solid("#59636e"),
                Border = Solid("#e2e6ea"),
                Accent = Solid("#2da44e"),
                AccentButton = Solid("#2c974b"),
                AccentText = Solid("#ffffff"),
                UserBubble = Solid("#eef4ff"),
                AssistantBubble = Solid("#f6f8fa"),
                Error = Solid("#cf222e"),
                Success = Solid("#2da44e")
            };
        }

        private static SolidColorBrush Solid(string hex)
        {
            return new SolidColorBrush(Color.Parse(hex));
        }
    }
}
