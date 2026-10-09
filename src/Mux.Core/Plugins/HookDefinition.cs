namespace Mux.Core.Plugins
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A user-authored hook: an external command mux runs out-of-process when a given
    /// <see cref="HookEventEnum"/> fires. The event payload is delivered as a JSON document on the hook's
    /// stdin; its stdout can be surfaced back into the session, and its exit code can veto a vetoable event.
    /// Hooks are the plugin system's event side — they extend mux without being loaded into its process.
    /// </summary>
    public sealed class HookDefinition
    {
        #region Private-Members

        private string _Command = string.Empty;
        private string _Matcher = string.Empty;
        private List<string> _Args = new List<string>();
        private int _TimeoutMs = 15000;

        #endregion

        #region Public-Members

        /// <summary>
        /// An optional human-readable name for the hook, used in listings and notices. Never null.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The lifecycle event this hook runs on.
        /// </summary>
        [JsonPropertyName("event")]
        public HookEventEnum Event { get; set; } = HookEventEnum.SessionStart;

        /// <summary>
        /// For tool events (<see cref="HookEventEnum.PreToolUse"/>, <see cref="HookEventEnum.PostToolUse"/>), the tool
        /// names this hook applies to: a glob with the same syntax as <c>--allow-tools</c> (<c>*</c>, <c>?</c>,
        /// case-insensitive, whole name), with <c>|</c> between alternatives. Empty matches every tool. Ignored for
        /// other events. Never null.
        /// </summary>
        [JsonPropertyName("matcher")]
        public string Matcher
        {
            get => _Matcher;
            set => _Matcher = value ?? string.Empty;
        }

        /// <summary>
        /// The executable to run (resolved against PATH). Never null; an empty command makes the hook invalid.
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
        /// Whether a non-zero exit from this hook on a vetoable event (for example
        /// <see cref="HookEventEnum.UserPromptSubmit"/>) blocks the action. Ignored for non-vetoable events.
        /// Defaults to false.
        /// </summary>
        [JsonPropertyName("blocking")]
        public bool Blocking { get; set; }

        /// <summary>
        /// The per-run timeout in milliseconds. Clamped to the range 100-600000. Defaults to 15000.
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
