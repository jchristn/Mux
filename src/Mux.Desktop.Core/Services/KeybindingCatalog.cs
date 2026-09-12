namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The default, front-end-agnostic list of bindable commands and their shipped chords. The ids and default
    /// chords mirror the TUI's command catalog (<c>MuxCommandCatalog</c>) so that overrides persisted to
    /// <c>keybindings.json</c> apply identically in the terminal and desktop front ends. This is data only — it
    /// carries no handlers and no TUIKit dependency, which keeps it in <c>Mux.Desktop.Core</c> and testable.
    /// </summary>
    public static class KeybindingCatalog
    {
        private static readonly IReadOnlyList<KeybindingCommand> _Commands = BuildDefaults();

        /// <summary>
        /// The bindable commands in a stable display order (grouped by the order below).
        /// </summary>
        public static IReadOnlyList<KeybindingCommand> Commands => _Commands;

        /// <summary>
        /// Finds a command by id, or null when none matches.
        /// </summary>
        /// <param name="id">The command id to look up.</param>
        /// <returns>The matching command, or null.</returns>
        public static KeybindingCommand? Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (KeybindingCommand command in _Commands)
            {
                if (string.Equals(command.Id, id, StringComparison.Ordinal))
                {
                    return command;
                }
            }

            return null;
        }

        private static IReadOnlyList<KeybindingCommand> BuildDefaults()
        {
            return new List<KeybindingCommand>
            {
                new KeybindingCommand("mux.quit", "Exit", "Session", "ctrl+q"),
                new KeybindingCommand("mux.save", "Save session", "Session", "ctrl+s"),
                new KeybindingCommand("mux.export", "Export session", "Session", null),
                new KeybindingCommand("mux.undo", "Undo last turn's changes", "Session", null),
                new KeybindingCommand("mux.redo", "Redo undone changes", "Session", null),
                new KeybindingCommand("mux.queue", "Edit queue", "Session", "ctrl+g"),
                new KeybindingCommand("mux.sessions", "Sessions", "Session", null),
                new KeybindingCommand("mux.compact", "Compact conversation", "Session", null),
                new KeybindingCommand("mux.endpoint", "Endpoints / models", "Model", "ctrl+e"),
                new KeybindingCommand("mux.prompts", "Prompts", "Model", "ctrl+p"),
                new KeybindingCommand("mux.mcp", "MCP servers", "Model", null),
                new KeybindingCommand("mux.skills", "Skills", "Model", null),
                new KeybindingCommand("mux.effort", "Reasoning effort", "Model", null),
                new KeybindingCommand("mux.settings", "Settings", "Model", null),
                new KeybindingCommand("mux.clear", "Clear transcript", "View", "ctrl+l"),
                new KeybindingCommand("mux.sidebar.toggle", "Toggle sidebar", "View", "ctrl+b"),
                new KeybindingCommand("mux.tasks", "Tasks", "View", null),
                new KeybindingCommand("mux.usage", "Usage", "View", null),
                new KeybindingCommand("mux.theme", "Theme", "View", null),
                new KeybindingCommand("mux.theme.dark", "Dark mode", "View", null),
                new KeybindingCommand("mux.theme.light", "Light mode", "View", null),
                new KeybindingCommand("mux.mouse", "Toggle mouse capture", "View", "f12"),
                new KeybindingCommand("mux.borders", "Toggle boundary lines", "View", null),
                new KeybindingCommand("mux.thinking", "Toggle thinking display", "View", null),
                new KeybindingCommand("mux.menu", "Command menu", "Help", "f1"),
                new KeybindingCommand("mux.help", "Help", "Help", null)
            };
        }
    }
}
