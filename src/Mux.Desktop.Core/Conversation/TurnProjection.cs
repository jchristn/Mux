namespace Mux.Desktop.Conversation
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Mux.Core.Agent;

    /// <summary>
    /// A UI-framework-agnostic accumulator that projects the <see cref="AgentEvent"/> stream of a single turn
    /// into rendering-ready state: streamed assistant text, streamed thinking, the tool-call list with
    /// statuses, the terminal error (if any), and the run-completed summary. Feed each event to
    /// <see cref="Apply"/>; a front end reads the properties to render. Pure and deterministic — no threading
    /// or UI assumptions — so it is unit-testable with synthetic events and shared across front ends.
    /// </summary>
    public sealed class TurnProjection
    {
        private const int ResultSummaryMaxLength = 240;

        private readonly StringBuilder _AssistantText = new StringBuilder();
        private readonly StringBuilder _ThinkingText = new StringBuilder();
        private readonly List<ToolCallRecord> _ToolCalls = new List<ToolCallRecord>();
        private bool _HasFirstToken;
        private bool _WasCancelled;
        private ErrorEvent? _Error;
        private RunCompletedEvent? _Completed;

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

        /// <summary>
        /// Apply one agent event, updating the projection. Unknown or purely transient events (heartbeats,
        /// context status) are ignored.
        /// </summary>
        /// <param name="agentEvent">The event to apply. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentEvent"/> is null.</exception>
        public void Apply(AgentEvent agentEvent)
        {
            ArgumentNullException.ThrowIfNull(agentEvent);

            switch (agentEvent)
            {
                case AssistantTextEvent text:
                    _AssistantText.Append(text.Text);
                    _HasFirstToken = true;
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
                case RunCompletedEvent runCompleted:
                    _Completed = runCompleted;
                    break;
                default:
                    // Heartbeats, context status, run-started, and task-plan updates carry no projection state.
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
    }
}
