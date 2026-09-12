namespace Mux.Desktop.Services
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Desktop-only preferences that are specific to the Avalonia client and are not part of the shared mux
    /// settings (which the TUI and server also read). Persisted to <c>desktop.json</c> in the config
    /// directory by <see cref="DesktopPreferencesStore"/>.
    /// </summary>
    public sealed class DesktopPreferences
    {
        /// <summary>
        /// The theme mode: <c>system</c>, <c>light</c>, or <c>dark</c>. Defaults to <c>dark</c>.
        /// </summary>
        [JsonPropertyName("themeMode")]
        public string ThemeMode { get; set; } = "dark";

        /// <summary>
        /// Whether the conversation sidebar starts collapsed to its icon rail. Defaults to false (expanded).
        /// </summary>
        [JsonPropertyName("sidebarCollapsed")]
        public bool SidebarCollapsed { get; set; }

        /// <summary>
        /// Whether the streamed thinking panel starts expanded rather than collapsed. Defaults to false.
        /// </summary>
        [JsonPropertyName("autoExpandThinking")]
        public bool AutoExpandThinking { get; set; }
    }
}
