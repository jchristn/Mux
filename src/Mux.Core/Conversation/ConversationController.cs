namespace Mux.Core.Conversation
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Telemetry;

    /// <summary>
    /// The front-end-agnostic controller that sequences an interactive session's turns: it owns the
    /// conversation history, the pending-prompt queue, and the per-turn stats, and it centralizes the policy
    /// for what enters history. Turn <em>execution</em> (building the loop, projecting events, rendering) is
    /// the front end's concern, supplied as a turn executor that runs one prompt and returns a
    /// <see cref="TurnOutcome"/>; the controller does the bookkeeping around it — hook gating, checkpoint
    /// recording, enqueue-or-run, history/stats updates, and starting the next queued prompt. It is genuinely
    /// async (no sync-over-async) so a UI dispatcher is never blocked, and it raises events with no thread
    /// guarantee so each front end marshals to its own dispatcher.
    /// </summary>
    /// <remarks>
    /// This is the shared centerpiece of the §4 extraction: it replaces the interactive shell's inline
    /// <c>EnqueueOrRun</c>/<c>RunTurn</c>/<c>OnTurnComplete</c> logic and gives both front ends one
    /// implementation of the turn/queue policy — including the rule that only a completed turn enters history
    /// (a cancelled/stopped or errored-with-no-completion turn is dropped so it never leaves a dangling user
    /// prompt for the next turn to batch), while a turn that ran to completion is kept even when its answer is
    /// empty so the prompt survives as context.
    /// </remarks>
    public sealed class ConversationController
    {
        #region Private-Members

        private readonly object _Sync = new object();
        private readonly List<ConversationMessage> _History = new List<ConversationMessage>();
        private readonly List<string> _Pending = new List<string>();
        private readonly ConversationStatsAggregator _Stats;
        private readonly Func<string, CancellationToken, Task<TurnOutcome>> _ExecuteTurn;
        private readonly Func<string, CancellationToken, Task>? _RecordCheckpoint;
        private readonly Func<string, bool>? _PassesHooks;

        private bool _TurnInFlight;
        private bool _QueuePaused;

        #endregion

        #region Public-Events

        /// <summary>Raised just before a turn's prompt begins executing (after checkpointing).</summary>
        public event Action<string>? TurnStarting;

        /// <summary>Raised after a turn finishes (completed, empty, or cancelled), with its outcome.</summary>
        public event Action<TurnOutcome>? TurnCompleted;

        /// <summary>Raised whenever the pending queue or busy state changes.</summary>
        public event Action? QueueChanged;

        /// <summary>Raised whenever the session stats change (after each recorded turn).</summary>
        public event Action? StatsChanged;

        /// <summary>Raised when the controller posts an informational notice for the front end to surface.</summary>
        public event Action<string>? NoticePosted;

        /// <summary>Raised when <see cref="Cancel"/> is called, so the front end can cancel its active turn.</summary>
        public event Action? CancelRequested;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the controller.
        /// </summary>
        /// <param name="seedHistory">Prior conversation history to resume from, or null for a fresh session.</param>
        /// <param name="stats">The stats aggregator the controller folds each turn into. Must not be null.</param>
        /// <param name="executeTurn">Runs one prompt to completion and returns its outcome. Must not be null.</param>
        /// <param name="recordCheckpoint">Optional per-turn checkpoint recorder invoked before each turn runs.</param>
        /// <param name="passesHooks">Optional pre-submit gate; when it returns false the prompt is vetoed.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ConversationController(
            IReadOnlyList<ConversationMessage>? seedHistory,
            ConversationStatsAggregator stats,
            Func<string, CancellationToken, Task<TurnOutcome>> executeTurn,
            Func<string, CancellationToken, Task>? recordCheckpoint = null,
            Func<string, bool>? passesHooks = null)
        {
            _Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _ExecuteTurn = executeTurn ?? throw new ArgumentNullException(nameof(executeTurn));
            _RecordCheckpoint = recordCheckpoint;
            _PassesHooks = passesHooks;
            if (seedHistory != null)
            {
                _History.AddRange(seedHistory);
            }
        }

        #endregion

        #region Public-Members

        /// <summary>Whether a turn is currently running.</summary>
        public bool IsBusy
        {
            get { lock (_Sync) { return _TurnInFlight; } }
        }

        /// <summary>Whether the queue is paused (no queued prompt starts until resumed).</summary>
        public bool IsQueuePaused
        {
            get { lock (_Sync) { return _QueuePaused; } }
        }

        /// <summary>The number of prompts waiting behind the running turn.</summary>
        public int QueuedCount
        {
            get { lock (_Sync) { return _Pending.Count; } }
        }

        /// <summary>The number of turns recorded this session.</summary>
        public int TurnCount
        {
            get { lock (_Sync) { return _Stats.Turns; } }
        }

        /// <summary>A snapshot copy of the pending prompts, in order.</summary>
        public IReadOnlyList<string> PendingPrompts
        {
            get { lock (_Sync) { return new List<string>(_Pending); } }
        }

        /// <summary>A snapshot copy of the conversation history.</summary>
        public IReadOnlyList<ConversationMessage> History
        {
            get { lock (_Sync) { return new List<ConversationMessage>(_History); } }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Submits a prompt: gates it on hooks, records a checkpoint, then runs it immediately when idle or
        /// queues it when a turn is in flight or the queue is paused. When it runs immediately, the returned
        /// task completes when the turn (and any it chains from the queue) finishes.
        /// </summary>
        /// <param name="prompt">The user prompt. Must not be null.</param>
        /// <param name="cancellationToken">A token to cancel the turn.</param>
        /// <returns>A task that completes when the started turn chain finishes (immediately when queued).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="prompt"/> is null.</exception>
        public async Task SubmitAsync(string prompt, CancellationToken cancellationToken)
        {
            if (prompt is null) throw new ArgumentNullException(nameof(prompt));

            if (_PassesHooks != null && !_PassesHooks(prompt))
            {
                return;
            }

            bool startNow;
            lock (_Sync)
            {
                if (!_TurnInFlight && !_QueuePaused)
                {
                    _TurnInFlight = true;
                    startNow = true;
                }
                else
                {
                    _Pending.Add(prompt);
                    startNow = false;
                }
            }

            if (startNow)
            {
                await RunAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                QueueChanged?.Invoke();
            }
        }

        /// <summary>
        /// Requests cancellation of the active turn (raises <see cref="CancelRequested"/> for the front end to
        /// cancel its running job).
        /// </summary>
        public void Cancel()
        {
            CancelRequested?.Invoke();
        }

        /// <summary>Pauses the queue so no queued prompt starts until <see cref="ResumeQueueAsync"/>.</summary>
        public void PauseQueue()
        {
            lock (_Sync)
            {
                _QueuePaused = true;
            }

            QueueChanged?.Invoke();
        }

        /// <summary>
        /// Resumes the queue; if the controller is idle and prompts remain, starts the next one in order.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel a started turn.</param>
        public async Task ResumeQueueAsync(CancellationToken cancellationToken)
        {
            string? next;
            lock (_Sync)
            {
                _QueuePaused = false;
                if (!_TurnInFlight && _Pending.Count > 0)
                {
                    _TurnInFlight = true;
                    next = _Pending[0];
                    _Pending.RemoveAt(0);
                }
                else
                {
                    next = null;
                }
            }

            QueueChanged?.Invoke();
            if (next != null)
            {
                await RunAsync(next, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Replaces the pending queue (for example after the queue editor closes).</summary>
        /// <param name="prompts">The new queue contents, in order. Null clears the queue.</param>
        public void SetPending(IEnumerable<string>? prompts)
        {
            lock (_Sync)
            {
                _Pending.Clear();
                if (prompts != null)
                {
                    _Pending.AddRange(prompts);
                }
            }

            QueueChanged?.Invoke();
        }

        /// <summary>Removes the pending prompt at the given index. Out-of-range indices are ignored.</summary>
        /// <param name="index">The zero-based index into the pending queue.</param>
        public void RemovePending(int index)
        {
            lock (_Sync)
            {
                if (index < 0 || index >= _Pending.Count)
                {
                    return;
                }

                _Pending.RemoveAt(index);
            }

            QueueChanged?.Invoke();
        }

        /// <summary>Moves a pending prompt from one position to another. Out-of-range indices are ignored.</summary>
        /// <param name="from">The current index.</param>
        /// <param name="to">The target index.</param>
        public void ReorderPending(int from, int to)
        {
            lock (_Sync)
            {
                if (from < 0 || from >= _Pending.Count || to < 0 || to >= _Pending.Count || from == to)
                {
                    return;
                }

                string item = _Pending[from];
                _Pending.RemoveAt(from);
                _Pending.Insert(to, item);
            }

            QueueChanged?.Invoke();
        }

        /// <summary>
        /// Renders the current session stats into a carrier, filling busy/queued from live state.
        /// </summary>
        /// <param name="taskTotal">The focused job's task count (0 when none).</param>
        /// <param name="taskCompleted">The focused job's completed-task count.</param>
        /// <param name="pricing">The pricing table for cost. Must not be null.</param>
        /// <param name="model">The active model name for pricing lookup.</param>
        /// <returns>A populated snapshot.</returns>
        public ConversationStats SnapshotStats(int taskTotal, int taskCompleted, PricingTable pricing, string? model)
        {
            lock (_Sync)
            {
                ConversationStats stats = _Stats.Snapshot(taskTotal, taskCompleted, pricing, model);
                stats.Busy = _TurnInFlight;
                stats.Queued = _Pending.Count;
                return stats;
            }
        }

        /// <summary>
        /// Posts an informational notice through <see cref="NoticePosted"/>.
        /// </summary>
        /// <param name="message">The notice text.</param>
        public void PostNotice(string message)
        {
            NoticePosted?.Invoke(message ?? string.Empty);
        }

        /// <summary>
        /// Replaces the conversation history and pending queue (for example when resuming a session).
        /// </summary>
        /// <param name="history">The history to restore, or null to clear.</param>
        /// <param name="pending">The pending prompts to restore, or null to clear.</param>
        public void Restore(IReadOnlyList<ConversationMessage>? history, IReadOnlyList<string>? pending)
        {
            lock (_Sync)
            {
                _History.Clear();
                if (history != null)
                {
                    _History.AddRange(history);
                }

                _Pending.Clear();
                if (pending != null)
                {
                    _Pending.AddRange(pending);
                }

                _TurnInFlight = false;
                _QueuePaused = false;
            }

            QueueChanged?.Invoke();
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(string prompt, CancellationToken cancellationToken)
        {
            if (_RecordCheckpoint != null)
            {
                try
                {
                    await _RecordCheckpoint(prompt, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Checkpointing is advisory; never block the turn on it.
                }
            }

            TurnStarting?.Invoke(prompt);

            TurnOutcome outcome;
            try
            {
                outcome = await _ExecuteTurn(prompt, cancellationToken).ConfigureAwait(false)
                    ?? new TurnOutcome { WasCancelled = true };
            }
            catch (OperationCanceledException)
            {
                outcome = new TurnOutcome { WasCancelled = true };
            }

            string? next;
            lock (_Sync)
            {
                // Record the exchange when the turn ran to completion — even if the answer is empty (keep the
                // prompt as context). Drop cancelled/stopped turns and turns that ended with no answer and no
                // completion (errored/timed out) so no dangling user prompt survives for the next turn to
                // batch.
                bool completed = outcome.RunCompleted != null;
                if (!outcome.WasCancelled && (!string.IsNullOrEmpty(outcome.AssistantText) || completed))
                {
                    _History.Add(new ConversationMessage { Role = RoleEnum.User, Content = prompt });
                    _History.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = outcome.AssistantText ?? string.Empty });
                }

                _Stats.RecordTurn(outcome.RunCompleted, outcome.TotalMs, outcome.TtftMs);

                if (!_QueuePaused && _Pending.Count > 0)
                {
                    next = _Pending[0];
                    _Pending.RemoveAt(0);
                }
                else
                {
                    next = null;
                    _TurnInFlight = false;
                }
            }

            StatsChanged?.Invoke();
            TurnCompleted?.Invoke(outcome);
            QueueChanged?.Invoke();

            if (next != null)
            {
                await RunAsync(next, cancellationToken).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
