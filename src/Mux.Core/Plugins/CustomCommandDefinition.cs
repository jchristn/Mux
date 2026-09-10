namespace Mux.Core.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A user-authored slash command backed by an external process. Registered onto the interactive command
    /// surface as <c>/&lt;name&gt;</c>; invoking it runs the command out-of-process in the working directory
    /// and surfaces its output into the transcript. This is the plugin system's command side — it adds
    /// operator-facing commands without any in-process code.
    /// </summary>
    public sealed class CustomCommandDefinition
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Command = string.Empty;
        private List<string> _Args = new List<string>();
        private int _TimeoutMs = 30000;

        #endregion

        #region Public-Members

        /// <summary>
        /// The slash-command name (without the leading slash), for example <c>"deploy"</c>. Never null; an
        /// empty name makes the command invalid.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// A short description shown in the command menu. Never null.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// The executable to run (resolved against PATH). Never null; an empty command makes the command invalid.
        /// </summary>
        [JsonPropertyName("command")]
        public string Command
        {
            get => _Command;
            set => _Command = value ?? string.Empty;
        }

        /// <summary>
        /// The arguments passed to the command. Never null.
        /// </summary>
        [JsonPropertyName("args")]
        public List<string> Args
        {
            get => _Args;
            set => _Args = value ?? new List<string>();
        }

        /// <summary>
        /// The per-run timeout in milliseconds. Clamped to the range 100-600000. Defaults to 30000.
        /// </summary>
        [JsonPropertyName("timeoutMs")]
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = Math.Clamp(value, 100, 600000);
        }

        #endregion
    }
}
