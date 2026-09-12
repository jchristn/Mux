namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Conversation;
    using Mux.Desktop.Conversation;

    /// <summary>
    /// Default <see cref="IConversationService"/> driving a single thread's turns through an
    /// <see cref="ITurnRunner"/>. Appends the user message, streams the turn into a
    /// <see cref="TurnProjection"/> (raising <see cref="Event"/> per event), and appends the assistant reply
    /// on completion. Genuinely asynchronous — no sync-over-async — so it is safe to await from a UI
    /// dispatcher. Not reentrant: one turn at a time per conversation.
    /// </summary>
    public sealed class ConversationService : IConversationService
    {
        private readonly ITurnRunner _Runner;
        private readonly List<ConversationMessage> _History;
        private TurnProjection? _CurrentTurn;
        private bool _IsBusy;

        /// <summary>
        /// Instantiate the conversation service.
        /// </summary>
        /// <param name="runner">The turn runner (engine seam). Required.</param>
        /// <param name="initialHistory">Prior history to resume from, or null for a fresh conversation.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runner"/> is null.</exception>
        public ConversationService(ITurnRunner runner, IEnumerable<ConversationMessage>? initialHistory)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
            _History = initialHistory != null
                ? new List<ConversationMessage>(initialHistory)
                : new List<ConversationMessage>();
        }

        /// <inheritdoc />
        public event EventHandler<AgentEvent>? Event;

        /// <inheritdoc />
        public event EventHandler? StateChanged;

        /// <inheritdoc />
        public IReadOnlyList<ConversationMessage> History
        {
            get => _History;
        }

        /// <inheritdoc />
        public bool IsBusy
        {
            get => _IsBusy;
        }

        /// <inheritdoc />
        public TurnProjection? CurrentTurn
        {
            get => _CurrentTurn;
        }

        /// <inheritdoc />
        public async Task<TurnProjection> RunTurnAsync(string prompt, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("Prompt must not be null or blank.", nameof(prompt));
            }

            if (_IsBusy)
            {
                throw new InvalidOperationException("A turn is already in flight for this conversation.");
            }

            _IsBusy = true;
            TurnProjection projection = new TurnProjection();
            _CurrentTurn = projection;

            // Snapshot the prior history (turns 1..N-1) before adding this turn's user message; the runner
            // receives the prior history and adds the new prompt itself, so the message is not duplicated.
            List<ConversationMessage> priorHistory = new List<ConversationMessage>(_History);

            ConversationMessage userMessage = new ConversationMessage
            {
                Role = RoleEnum.User,
                Content = prompt
            };
            _History.Add(userMessage);
            RaiseStateChanged();

            try
            {
                await foreach (AgentEvent agentEvent in _Runner.RunAsync(prompt, priorHistory, token).ConfigureAwait(false))
                {
                    projection.Apply(agentEvent);
                    Event?.Invoke(this, agentEvent);
                }
            }
            catch (OperationCanceledException)
            {
                projection.MarkCancelled();
            }
            finally
            {
                // Treat a token-cancelled turn as cancelled even if the loop returned gracefully (so a turn
                // stopped after streaming partial text is still dropped, not kept).
                bool cancelled = projection.WasCancelled || token.IsCancellationRequested;
                AppendAssistantReply(projection, userMessage, cancelled);
                _IsBusy = false;
                RaiseStateChanged();
            }

            return projection;
        }

        private void AppendAssistantReply(TurnProjection projection, ConversationMessage userMessage, bool cancelled)
        {
            string answer = projection.AssistantText;
            if (cancelled || string.IsNullOrEmpty(answer))
            {
                // The turn did not complete a normal exchange — it was cancelled/stopped, errored, or the
                // model ended in its reasoning channel without a final answer. Drop the user message added
                // before the turn so the model-facing history never carries a dangling, unanswered prompt;
                // otherwise the next turn would send the model a run of consecutive user messages, which it
                // treats as one batched request. The transcript still shows the user's bubble (rendered by the
                // shell directly).
                _History.Remove(userMessage);
                return;
            }

            _History.Add(new ConversationMessage
            {
                Role = RoleEnum.Assistant,
                Content = answer
            });
        }

        private void RaiseStateChanged()
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
