namespace Mux.Core.Agent
{
    using Mux.Core.Enums;

    /// <summary>
    /// Reports what a <c>pre-tool-use</c>, <c>post-tool-use</c>, or <c>stop</c> hook did during a run: blocked a
    /// tool call, added feedback to a tool result, asked the model to continue, or failed (a warning; the run goes on).
    /// </summary>
    public class HookEvent : AgentEvent
    {
        #region Public-Members

        /// <summary>The blocked outcome: a pre-tool-use hook exited 2 and the tool did not run.</summary>
        public const string OutcomeBlocked = "blocked";

        /// <summary>The appended outcome: a post-tool-use hook added text to the tool result.</summary>
        public const string OutcomeAppended = "appended";

        /// <summary>The continued outcome: a stop hook exited 2 and the model was asked to continue.</summary>
        public const string OutcomeContinued = "continued";

        /// <summary>The warning outcome: a hook failed to start, timed out, or exited with an unexpected code.</summary>
        public const string OutcomeWarning = "warning";

        /// <summary>
        /// Initializes a new instance of the <see cref="HookEvent"/> class.
        /// </summary>
        public HookEvent()
        {
            EventType = AgentEventTypeEnum.Hook;
        }

        /// <summary>The hook event wire name (<c>pre-tool-use</c>, <c>post-tool-use</c>, or <c>stop</c>).</summary>
        public string HookEventName { get; set; } = string.Empty;

        /// <summary>The hook's name, or its command when unnamed.</summary>
        public string HookName { get; set; } = string.Empty;

        /// <summary>What happened: one of the <c>Outcome*</c> constants.</summary>
        public string Outcome { get; set; } = OutcomeWarning;

        /// <summary>The hook's exit code (124 on timeout, 127 when it could not start).</summary>
        public int ExitCode { get; set; }

        /// <summary>The tool the hook ran for, or empty for a stop hook.</summary>
        public string ToolName { get; set; } = string.Empty;

        /// <summary>The tool call the hook ran for, or empty for a stop hook.</summary>
        public string ToolCallId { get; set; } = string.Empty;

        /// <summary>A short human-readable explanation, including the hook's output where relevant.</summary>
        public string Message { get; set; } = string.Empty;

        #endregion
    }
}
