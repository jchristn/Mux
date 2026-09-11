namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Desktop.Conversation;

    /// <summary>
    /// A test <see cref="ITurnRunner"/> that yields a fixed list of synthetic agent events, optionally
    /// throwing <see cref="OperationCanceledException"/> to simulate a cancelled turn. Lets the conversation
    /// orchestration be exercised without a live model backend.
    /// </summary>
    public sealed class ListTurnRunner : ITurnRunner
    {
        private readonly IReadOnlyList<AgentEvent> _Events;
        private readonly bool _Cancel;

        /// <summary>
        /// Instantiate a runner that yields the given events.
        /// </summary>
        /// <param name="events">The events to yield in order. Null is treated as empty.</param>
        /// <param name="cancel">When true, throws <see cref="OperationCanceledException"/> instead of yielding.</param>
        public ListTurnRunner(IReadOnlyList<AgentEvent>? events, bool cancel = false)
        {
            _Events = events ?? new List<AgentEvent>();
            _Cancel = cancel;
        }

        /// <summary>
        /// Yield the configured events, honoring cancellation.
        /// </summary>
        /// <param name="prompt">The prompt (ignored).</param>
        /// <param name="history">The history (ignored).</param>
        /// <param name="token">A cancellation token.</param>
        /// <returns>The synthetic event stream.</returns>
        public async IAsyncEnumerable<AgentEvent> RunAsync(
            string prompt,
            IReadOnlyList<ConversationMessage> history,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (_Cancel)
            {
                throw new OperationCanceledException();
            }

            foreach (AgentEvent agentEvent in _Events)
            {
                token.ThrowIfCancellationRequested();
                yield return agentEvent;
                await Task.Yield();
            }
        }
    }
}
