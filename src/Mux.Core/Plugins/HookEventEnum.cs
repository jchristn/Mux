namespace Mux.Core.Plugins
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The lifecycle events that out-of-process hooks can subscribe to. Kept deliberately small and
    /// well-defined: each event names a concrete moment in a session at which mux runs the matching hooks.
    /// </summary>
    [JsonConverter(typeof(HookEventEnumConverter))]
    public enum HookEventEnum
    {
        /// <summary>
        /// Fired once when an interactive session starts, before the first prompt.
        /// </summary>
        SessionStart,

        /// <summary>
        /// Fired when the user submits a prompt, before the turn runs. A hook that exits non-zero vetoes
        /// the submission.
        /// </summary>
        UserPromptSubmit,

        /// <summary>
        /// Fired once when an interactive session ends.
        /// </summary>
        SessionEnd,

        /// <summary>
        /// Fired after a tool call is approved and before it runs. Exit 2 blocks the call and returns the hook's
        /// stderr to the model as the tool result; any other non-zero exit is logged and the call continues.
        /// </summary>
        PreToolUse,

        /// <summary>
        /// Fired after a tool call runs. Standard output (exit 0) or standard error (exit 2) is appended to the
        /// tool result the model sees.
        /// </summary>
        PostToolUse,

        /// <summary>
        /// Fired when the model finishes a run. Exit 2 asks the model to continue with the hook's stderr as a new
        /// user message, at most three times per run.
        /// </summary>
        Stop
    }
}
