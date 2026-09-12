namespace Mux.Core.Conversation
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Core.Agent;

    /// <summary>
    /// A UI-framework-agnostic accumulator that projects the <see cref="AgentEvent"/> stream of a single turn
    /// into rendering-ready state: streamed assistant text, streamed thinking, the tool-call list with
    /// statuses, the terminal error (if any), and the run-completed summary. Feed each event to
    /// <see cref="Apply"/>; a front end reads the properties to render (the desktop rebuilds its view from
    /// this state) and/or subscribes to the streaming signals (the TUI dismisses and resumes its "thinking"
    /// indicator from them). Pure and deterministic apart from those signal callbacks — no threading or UI
    /// assumptions — so it is unit-testable with synthetic events and shared across both front ends.
    /// </summary>
    /// <remarks>
    /// This is the single shared turn accumulator (§4.2). The desktop reads the state properties; the TUI's
    /// <c>AgentEventProjector</c> reads <see cref="AssistantText"/>/<see cref="Completed"/>/<see cref="Error"/>
    /// after the turn and drives its indicator from <see cref="FirstTokenReceived"/>/<see cref="ModelResponded"/>/
    /// <see cref="ModelWorking"/>. The signal events carry no thread guarantee; each front end marshals to its
    /// own dispatcher.
    /// </remarks>
    public sealed class TurnProjection
    {
        #region Private-Members

        private const int ResultSummaryMaxLength = 240;

        private readonly StringBuilder _AssistantText = new StringBuilder();
        private readonly StringBuilder _ThinkingText = new StringBuilder();
        private readonly List<ToolCallRecord> _ToolCalls = new List<ToolCallRecord>();
        private bool _HasFirstToken;
        private bool _FirstTokenSignalled;
        private bool _ModelResponded;
        private bool _WasCancelled;
        private ErrorEvent? _Error;
        private RunCompletedEvent? _Completed;

        #endregion

        #region Public-Events

        /// <summary>
        /// Raised once, when the first non-empty assistant token of the run is applied. Used by a front end to
        /// stamp time-to-first-token.
        /// </summary>
        public event Action? FirstTokenReceived;

        /// <summary>
        /// Raised when the run produces observable output (assistant text or thinking, a tool call, or an
        /// error) after a quiet stretch. Used to dismiss a "thinking" indicator the moment results begin.
        /// Re-armed by a heartbeat, so it fires again once the model resumes after a step's tool calls.
        /// </summary>
        public event Action? ModelResponded;

        /// <summary>
        /// Raised on a heartbeat — the model is about to be called again after a step's tool calls, with
        /// nothing yet to show. Used to bring a "thinking" indicator back so a working turn never looks
        /// stalled between tool runs.
        /// </summary>
        public event Action? ModelWorking;

        #endregion

        #region Public-Members

        /// <summary>The accumulated assistant answer text.</summary>
        public string AssistantText
        {
            get => _AssistantText.ToString();
        }

        /// <summary>The accumulated assistant thinking/reasoning text.</summary>
        public string ThinkingText
        {
            get => _ThinkingText.ToString();
        }

        /// <summary>The tool calls seen this turn, in proposal order, with their current statuses.</summary>
        public IReadOnlyList<ToolCallRecord> ToolCalls
        {
            get => _ToolCalls;
        }

        /// <summary>True once the first assistant text chunk has arrived.</summary>
        public bool HasFirstToken
        {
            get => _HasFirstToken;
        }

        /// <summary>True when the turn was cancelled before completing.</summary>
        public bool WasCancelled
        {
            get => _WasCancelled;
        }

        /// <summary>The terminal error for the turn, or null when none occurred.</summary>
        public ErrorEvent? Error
        {
            get => _Error;
        }

        /// <summary>The run-completed summary, or null until the turn completes.</summary>
        public RunCompletedEvent? Completed
        {
            get => _Completed;
        }

        /// <summary>True when a run-completed event has been applied.</summary>
        public bool IsComplete
        {
            get => _Completed != null;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply one agent event, updating the projection and raising any streaming signals it triggers.
        /// Unknown or purely transient events (context status) carry no projection state; a heartbeat carries
        /// no state but re-arms the responded latch and raises <see cref="ModelWorking"/>.
        /// </summary>
        /// <param name="agentEvent">The event to apply. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentEvent"/> is null.</exception>
        public void Apply(AgentEvent agentEvent)
        {
            if (agentEvent is null) throw new ArgumentNullException(nameof(agentEvent));

            SignalRespondedIfMeaningful(agentEvent);

            switch (agentEvent)
            {
                case AssistantTextEvent text:
                    _AssistantText.Append(text.Text);
                    _HasFirstToken = true;
                    if (!_FirstTokenSignalled && !string.IsNullOrEmpty(text.Text))
                    {
                        _FirstTokenSignalled = true;
                        FirstTokenReceived?.Invoke();
                    }

                    break;
                case AssistantThinkingEvent thinking:
                    _ThinkingText.Append(thinking.Text);
                    break;
                case ToolCallProposedEvent proposed:
                    _ToolCalls.Add(new ToolCallRecord(proposed.ToolCall));
                    break;
                case ToolCallApprovedEvent approved:
                    ApplyApproved(approved.ToolCallId);
                    break;
                case ToolCallCompletedEvent completed:
                    ApplyCompleted(completed);
                    break;
                case ErrorEvent error:
                    _Error = error;
                    break;
                case HeartbeatEvent:
                    // A step's tool calls are done and the model is about to be called again. Re-arm the
                    // "responded" latch and raise ModelWorking so a front end can resume its wait indicator.
                    _ModelResponded = false;
                    ModelWorking?.Invoke();
                    break;
                case RunCompletedEvent runCompleted:
                    _Completed = runCompleted;
                    break;
                default:
                    // Context status, run-started, and task-plan updates carry no projection state.
                    break;
            }
        }

        /// <summary>
        /// Mark the turn as cancelled. Called by the driver when the turn's enumeration is cancelled.
        /// </summary>
        public void MarkCancelled()
        {
            _WasCancelled = true;
        }

        #endregion

        #region Private-Methods

        private void SignalRespondedIfMeaningful(AgentEvent agentEvent)
        {
            if (_ModelResponded)
            {
                return;
            }

            if (agentEvent is AssistantTextEvent
                || agentEvent is AssistantThinkingEvent
                || agentEvent is ToolCallProposedEvent
                || agentEvent is ToolCallCompletedEvent
                || agentEvent is ErrorEvent
                || agentEvent is TaskPlanUpdatedEvent)
            {
                _ModelResponded = true;
                ModelResponded?.Invoke();
            }
        }

        private void ApplyApproved(string toolCallId)
        {
            ToolCallRecord? record = FindLatest(toolCallId);
            if (record != null && record.Status == ToolCallStatus.Running)
            {
                record.Status = ToolCallStatus.Approved;
            }
        }

        private void ApplyCompleted(ToolCallCompletedEvent completed)
        {
            ToolCallRecord? record = FindLatest(completed.ToolCallId);
            if (record == null)
            {
                return;
            }

            record.Status = completed.Result.Success ? ToolCallStatus.Completed : ToolCallStatus.Failed;
            record.ElapsedMs = completed.ElapsedMs;
            record.ResultSummary = Summarize(completed.Result.Content);
        }

        private ToolCallRecord? FindLatest(string toolCallId)
        {
            if (string.IsNullOrEmpty(toolCallId))
            {
                return null;
            }

            for (int i = _ToolCalls.Count - 1; i >= 0; i--)
            {
                if (string.Equals(_ToolCalls[i].Id, toolCallId, StringComparison.Ordinal))
                {
                    return _ToolCalls[i];
                }
            }

            return null;
        }

        private static string Summarize(string? content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }

            string trimmed = content.Trim();
            if (trimmed.Length <= ResultSummaryMaxLength)
            {
                return trimmed;
            }

            return trimmed.Substring(0, ResultSummaryMaxLength) + "…";
        }

        #endregion
    }
}
