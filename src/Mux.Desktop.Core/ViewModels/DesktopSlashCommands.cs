namespace Mux.Desktop.ViewModels
{
    using System.Collections.Generic;

    /// <summary>Public slash aliases available in the desktop conversation composer.</summary>
    public static class DesktopSlashCommands
    {
        /// <summary>Supported desktop aliases, without the leading slash.</summary>
        public static IReadOnlyList<string> Aliases { get; } = new[]
        {
            "cwd", "cd", "chdir", "clear", "context", "stats",
            "usage", "endpoints", "endpoint", "models", "model", "mcp",
            "mcps", "prompt", "prompts", "skill", "skills", "subagent",
            "subagents", "agents", "commands", "palette", "pricing", "search",
            "websearch", "plugin", "plugins", "hooks", "keys", "keybindings",
            "shortcuts", "undo", "redo", "effort", "compact", "new",
            "label", "labels", "tag", "tags", "help", "?",
            "menu", "trust", "instructions", "memory", "memories", "plan",
            "worktrees", "processes", "ps"
        };
    }
}
