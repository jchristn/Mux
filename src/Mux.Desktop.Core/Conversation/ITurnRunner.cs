namespace Mux.Desktop.Conversation
{
    using System.Collections.Generic;
    using System.Threading;
    using Mux.Core.Agent;
    using Mux.Core.Models;

    /// <summary>
    /// Runs one agent turn and streams its <see cref="AgentEvent"/> sequence. This is the seam between the
    /// front-end-agnostic <c>ConversationService</c> and the engine: the real implementation builds an
    /// <c>AgentLoopOptions</c> and drives <c>AgentLoop.RunAsync</c>, while tests supply a fake that yields a
    /// synthetic event stream. Keeping the driver behind this interface makes the conversation orchestration
    /// unit-testable without a live model backend.
    /// </summary>
    public interface ITurnRunner
    {
        /// <summary>
        /// Run one user turn against the accumulated history and stream the resulting events.
        /// </summary>
        /// <param name="prompt">The user prompt for this turn.</param>
        /// <param name="history">The prior conversation history (turns 1..N-1).</param>
        /// <param name="token">A token to cancel the turn.</param>
        /// <returns>The streamed agent events for the turn.</returns>
        IAsyncEnumerable<AgentEvent> RunAsync(string prompt, IReadOnlyList<ConversationMessage> history, CancellationToken token);
    }
}
