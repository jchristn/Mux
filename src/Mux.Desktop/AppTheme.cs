namespace Mux.Desktop
{
    using System;
    using Avalonia.Media;

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
        public static AppTheme Current { get; private set; } = CreateDark();

        /// <summary>
        /// Swap the active palette between light and dark.
        /// </summary>
        /// <returns>The new active palette.</returns>
        public static AppTheme Toggle()
        {
            Current = Current.IsDark ? CreateLight() : CreateDark();
            Changed?.Invoke(null, EventArgs.Empty);
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
            Changed?.Invoke(null, EventArgs.Empty);
            return Current;
        }

        /// <summary>
        /// Set the active palette to the high-contrast accessibility variant.
        /// </summary>
        /// <returns>The new active palette.</returns>
        public static AppTheme SetHighContrast()
        {
            Current = CreateHighContrast();
            Changed?.Invoke(null, EventArgs.Empty);
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
