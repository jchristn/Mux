namespace Mux.Core.Conversation
{
    using Mux.Core.Agent;

    /// <summary>
    /// The result of executing a single turn, produced by a front end's turn executor and handed back to
    /// <see cref="ConversationController"/> so it can update history, stats, and the queue. Front-end
    /// rendering is the executor's concern; this carries only the outcome the controller needs.
    /// </summary>
    public sealed class TurnOutcome
    {
        /// <summary>The assistant's final answer text, or null/empty when the turn produced none.</summary>
        public string? AssistantText { get; set; }

        /// <summary>Whether the turn was cancelled/stopped before completing a normal exchange.</summary>
        public bool WasCancelled { get; set; }

        /// <summary>The run's end-of-run summary (token counts, estimated context), or null when none.</summary>
        public RunCompletedEvent? RunCompleted { get; set; }

        /// <summary>Wall-clock duration of the turn, in milliseconds.</summary>
        public long TotalMs { get; set; }

        /// <summary>Time-to-first-token for the turn, in milliseconds (negative when none).</summary>
        public long TtftMs { get; set; } = -1;
    }
}
