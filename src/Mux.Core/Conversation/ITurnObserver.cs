namespace Mux.Core.Conversation
{
    using Mux.Core.Agent;

    /// <summary>
    /// The rendering seam for a single turn. A front end supplies an implementation that paints a job's
    /// <see cref="AgentEvent"/> stream onto its own surface (a TUIKit pane, an Avalonia transcript, etc.);
    /// the accumulation of run-level state (captured text, completion summary, error/cancel flags, timing
    /// signals) is handled separately by <see cref="TurnProjection"/> so it need not be reimplemented per
    /// front end. The driver (the TUI projector today, the conversation controller once turns are
    /// centralized) ingests each event into a <see cref="TurnProjection"/> and hands the same event to the
    /// observer for rendering, then calls <see cref="OnCompleted"/> or <see cref="OnCancelled"/> when the
    /// stream ends. Implementations carry no thread guarantee; each front end marshals to its own dispatcher.
    /// </summary>
    public interface ITurnObserver
    {
        /// <summary>
        /// Renders one agent event to the front end's surface. Called once per event in stream order.
        /// </summary>
        /// <param name="agentEvent">The event to render. Never null.</param>
        void OnEvent(AgentEvent agentEvent);

        /// <summary>
        /// Signals that the event stream ended normally. The observer finalizes any buffered block (for
        /// example re-rendering the trailing assistant text as Markdown).
        /// </summary>
        void OnCompleted();

        /// <summary>
        /// Signals that the event stream ended because the turn was cancelled. The observer finalizes any
        /// buffered block and surfaces a cancellation notice.
        /// </summary>
        void OnCancelled();
    }
}
