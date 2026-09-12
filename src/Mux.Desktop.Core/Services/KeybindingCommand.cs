namespace Mux.Desktop.Services
{
    /// <summary>
    /// A single bindable command in the shared keybinding catalog: its stable id (the key written into
    /// <c>keybindings.json</c>), a human-readable title, a grouping category, and the default key chord it
    /// ships with (null when the command has no default chord). Ids mirror the TUI's command surface so a
    /// <c>keybindings.json</c> authored in either front end is portable to the other.
    /// </summary>
    public sealed class KeybindingCommand
    {
        /// <summary>
        /// Instantiate a bindable command descriptor.
        /// </summary>
        /// <param name="id">The stable command id (the override key in <c>keybindings.json</c>). Required.</param>
        /// <param name="title">The human-readable command title. Required.</param>
        /// <param name="category">The grouping category (for example <c>View</c> or <c>Session</c>). Required.</param>
        /// <param name="defaultChord">The default key chord, or null when the command has none.</param>
        public KeybindingCommand(string id, string title, string category, string? defaultChord)
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Category = category ?? string.Empty;
            DefaultChord = string.IsNullOrWhiteSpace(defaultChord) ? null : defaultChord;
        }

        /// <summary>The stable command id (the override key in <c>keybindings.json</c>).</summary>
        public string Id { get; }

        /// <summary>The human-readable command title.</summary>
        public string Title { get; }

        /// <summary>The grouping category.</summary>
        public string Category { get; }

        /// <summary>The default key chord, or null when the command has none.</summary>
        public string? DefaultChord { get; }
    }
}
