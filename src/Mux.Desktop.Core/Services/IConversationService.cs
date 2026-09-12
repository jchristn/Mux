namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Core.Conversation;
    using Mux.Desktop.Conversation;

    /// <summary>
    /// Drives agent turns for a single conversation (thread): accumulates history, runs a turn through the
    /// engine seam, projects the streamed events, and appends the assistant reply. View models observe
    /// <see cref="Event"/> for live rendering and <see cref="StateChanged"/> for busy/idle transitions.
    /// </summary>
    public interface IConversationService
    {
        /// <summary>The accumulated conversation history.</summary>
        IReadOnlyList<ConversationMessage> History { get; }

        /// <summary>True while a turn is in flight.</summary>
        bool IsBusy { get; }

        /// <summary>The projection of the most recent (or in-flight) turn, or null before the first turn.</summary>
        TurnProjection? CurrentTurn { get; }

        /// <summary>Raised for each streamed agent event so a front end can render live.</summary>
        event EventHandler<AgentEvent>? Event;

        /// <summary>Raised when <see cref="IsBusy"/> changes or a turn completes.</summary>
        event EventHandler? StateChanged;

        /// <summary>
        /// Submit one user turn and stream it to completion.
        /// </summary>
        /// <param name="prompt">The user prompt. Must be non-empty.</param>
        /// <param name="token">A token to cancel the turn.</param>
        /// <returns>The projection of the completed (or cancelled) turn.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="prompt"/> is null or blank.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a turn is already in flight.</exception>
        Task<TurnProjection> RunTurnAsync(string prompt, CancellationToken token);
    }
}
