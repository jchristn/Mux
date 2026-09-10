namespace Mux.Core.Plugins
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The parsed contents of <c>~/.mux/hooks.json</c>: the user's event hooks and custom slash commands.
    /// Deserialization is forward-tolerant of unknown fields.
    /// </summary>
    public sealed class PluginConfig
    {
        #region Private-Members

        private List<HookDefinition> _Hooks = new List<HookDefinition>();
        private List<CustomCommandDefinition> _Commands = new List<CustomCommandDefinition>();

        #endregion

        #region Public-Members

        /// <summary>
        /// The configured event hooks. Never null.
        /// </summary>
        [JsonPropertyName("hooks")]
        public List<HookDefinition> Hooks
        {
            get => _Hooks;
            set => _Hooks = value ?? new List<HookDefinition>();
        }

        /// <summary>
        /// The configured custom slash commands. Never null.
        /// </summary>
        [JsonPropertyName("commands")]
        public List<CustomCommandDefinition> Commands
        {
            get => _Commands;
            set => _Commands = value ?? new List<CustomCommandDefinition>();
        }

        #endregion
    }
}
