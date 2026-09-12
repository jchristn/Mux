namespace Mux.Core.Conversation
{
    using System;
    using Mux.Core.Models;

    /// <summary>
    /// The UI-facing state of a single tool call within a turn: its identity, arguments, current status,
    /// elapsed time, and a short result summary. Mutated in place by <see cref="TurnProjection"/> as the
    /// proposed → approved → completed events arrive.
    /// </summary>
    public sealed class ToolCallRecord
    {
        /// <summary>
        /// Instantiate a record for a proposed tool call.
        /// </summary>
        /// <param name="toolCall">The proposed tool call. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolCall"/> is null.</exception>
        public ToolCallRecord(ToolCall toolCall)
        {
            if (toolCall is null) throw new ArgumentNullException(nameof(toolCall));

            _Id = toolCall.Id ?? string.Empty;
            _Name = toolCall.Name ?? string.Empty;
            _Arguments = toolCall.Arguments ?? string.Empty;
            _Status = ToolCallStatus.Running;
        }

        private string _Id;
        private string _Name;
        private string _Arguments;
        private ToolCallStatus _Status;
        private long _ElapsedMs;
        private string _ResultSummary = string.Empty;

        /// <summary>The tool-call identifier.</summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>The tool name.</summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>The raw JSON arguments proposed for the call.</summary>
        public string Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? string.Empty;
        }

        /// <summary>The current status of the call.</summary>
        public ToolCallStatus Status
        {
            get => _Status;
            set => _Status = value;
        }

        /// <summary>The elapsed execution time in milliseconds (0 until completed).</summary>
        public long ElapsedMs
        {
            get => _ElapsedMs;
            set => _ElapsedMs = value;
        }

        /// <summary>A short, truncated summary of the tool result.</summary>
        public string ResultSummary
        {
            get => _ResultSummary;
            set => _ResultSummary = value ?? string.Empty;
        }
    }
}
