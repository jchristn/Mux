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
    }
}
