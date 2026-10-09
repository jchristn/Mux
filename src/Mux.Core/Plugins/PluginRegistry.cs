namespace Mux.Core.Plugins
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An in-memory view over a <see cref="PluginConfig"/>: the valid hooks grouped by event and the valid
    /// custom commands indexed by name. Invalid entries (an empty command, or a command with an empty name)
    /// are dropped at construction, and a later custom command duplicating an earlier name is ignored, so
    /// consumers only ever see usable plugins.
    /// </summary>
    public sealed class PluginRegistry
    {
        #region Private-Members

        private readonly List<HookDefinition> _Hooks = new List<HookDefinition>();
        private readonly List<CustomCommandDefinition> _Commands = new List<CustomCommandDefinition>();
        private readonly Dictionary<string, CustomCommandDefinition> _CommandsByName = new Dictionary<string, CustomCommandDefinition>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginRegistry"/> class from a configuration.
        /// </summary>
        /// <param name="config">The plugin configuration, or null for an empty registry.</param>
        public PluginRegistry(PluginConfig? config)
        {
            if (config == null)
            {
                return;
            }

            foreach (HookDefinition hook in config.Hooks)
            {
                if (hook != null && !string.IsNullOrWhiteSpace(hook.Command))
                {
                    _Hooks.Add(hook);
                }
            }

            foreach (CustomCommandDefinition command in config.Commands)
            {
                if (command == null || string.IsNullOrWhiteSpace(command.Name) || string.IsNullOrWhiteSpace(command.Command))
                {
                    continue;
                }

                string trimmedName = command.Name.Trim().TrimStart('/');
                if (trimmedName.Length == 0 || _CommandsByName.ContainsKey(trimmedName))
                {
                    continue;
                }

                command.Name = trimmedName;
                _Commands.Add(command);
                _CommandsByName[trimmedName] = command;
            }
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The valid hooks in configuration order.
        /// </summary>
        public IReadOnlyList<HookDefinition> Hooks
        {
            get => _Hooks;
        }

        /// <summary>
        /// The valid custom commands in configuration order.
        /// </summary>
        public IReadOnlyList<CustomCommandDefinition> Commands
        {
            get => _Commands;
        }

        /// <summary>
        /// Whether the registry has any hooks or custom commands.
        /// </summary>
        public bool IsEmpty
        {
            get => _Hooks.Count == 0 && _Commands.Count == 0;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the hooks registered for a given event, in configuration order.
        /// </summary>
        /// <param name="hookEvent">The event to filter by.</param>
        /// <returns>The matching hooks (possibly empty).</returns>
        public IReadOnlyList<HookDefinition> HooksFor(HookEventEnum hookEvent)
        {
            List<HookDefinition> matches = new List<HookDefinition>();
            foreach (HookDefinition hook in _Hooks)
            {
                if (hook.Event == hookEvent)
                {
                    matches.Add(hook);
                }
            }

            return matches;
        }

        /// <summary>
        /// Loads <c>hooks.json</c> from the config directory for an agent loop's tool and stop hooks. Returns null
        /// when no hooks are configured or the file cannot be read, so a broken file never fails a run.
        /// </summary>
        /// <returns>The registry, or null.</returns>
        public static PluginRegistry? LoadHooksOrNull()
        {
            try
            {
                PluginRegistry registry = new PluginRegistry(Mux.Core.Settings.SettingsLoader.LoadPluginConfig());
                return registry.Hooks.Count == 0 ? null : registry;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the hooks registered for a tool event whose <see cref="HookDefinition.Matcher"/> matches the tool
        /// name, in configuration order.
        /// </summary>
        /// <param name="hookEvent">The event to filter by.</param>
        /// <param name="toolName">The tool name to match.</param>
        /// <returns>The matching hooks (possibly empty).</returns>
        public IReadOnlyList<HookDefinition> HooksFor(HookEventEnum hookEvent, string? toolName)
        {
            List<HookDefinition> matches = new List<HookDefinition>();
            foreach (HookDefinition hook in _Hooks)
            {
                if (hook.Event == hookEvent && Mux.Core.Agent.ToolGovernance.MatchesToolPattern(toolName, hook.Matcher))
                {
                    matches.Add(hook);
                }
            }

            return matches;
        }

        /// <summary>
        /// Finds a custom command by name (case-insensitive), or returns null when none is registered.
        /// </summary>
        /// <param name="name">The command name (with or without a leading slash).</param>
        /// <returns>The matching command, or null.</returns>
        public CustomCommandDefinition? FindCommand(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string key = name.Trim().TrimStart('/');
            return _CommandsByName.TryGetValue(key, out CustomCommandDefinition? command) ? command : null;
        }

        #endregion
    }
}
