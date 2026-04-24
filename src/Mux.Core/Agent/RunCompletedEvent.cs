namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event emitted when a mux run completes with a final status summary.
    /// </summary>
    public class RunCompletedEvent : AgentEvent
    {
        #region Private-Members

        private string _RunId = string.Empty;
        private string _Status = string.Empty;
        private int _IterationsCompleted = 0;
        private int _ToolCallCount = 0;
        private int _ErrorCount = 0;
        private int _AssistantTextChars = 0;
        private long _DurationMs = 0;
        private int _FinalEstimatedTokens = 0;
        private int _CompactionCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="RunCompletedEvent"/> class.
        /// </summary>
        public RunCompletedEvent()
        {
            EventType = AgentEventTypeEnum.RunCompleted;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Correlation identifier for the run.
        /// </summary>
        public string RunId
        {
            get => _RunId;
            set => _RunId = value ?? throw new ArgumentNullException(nameof(RunId));
        }

        /// <summary>
        /// Final run status such as completed, completed_with_errors, or max_iterations_reached.
        /// </summary>
        public string Status
        {
            get => _Status;
            set => _Status = value ?? string.Empty;
        }

        /// <summary>
        /// Total iterations that were processed.
        /// </summary>
        public int IterationsCompleted
        {
            get => _IterationsCompleted;
            set => _IterationsCompleted = value;
        }

        /// <summary>
        /// Total proposed tool calls handled during the run.
        /// </summary>
        public int ToolCallCount
        {
            get => _ToolCallCount;
            set => _ToolCallCount = value;
        }

        /// <summary>
        /// Total error events emitted during the run.
        /// </summary>
        public int ErrorCount
        {
            get => _ErrorCount;
            set => _ErrorCount = value;
        }

        /// <summary>
        /// Total assistant text characters emitted during the run.
        /// </summary>
        public int AssistantTextChars
        {
            get => _AssistantTextChars;
            set => _AssistantTextChars = value;
        }

        /// <summary>
        /// Total wall-clock duration of the run.
        /// </summary>
        public long DurationMs
        {
            get => _DurationMs;
            set => _DurationMs = value;
        }

        /// <summary>
        /// Estimated used tokens in the final conversation state at the end of the run.
        /// </summary>
        public int FinalEstimatedTokens
        {
            get => _FinalEstimatedTokens;
            set => _FinalEstimatedTokens = value;
        }

        /// <summary>
        /// Total number of compaction passes applied during the run.
        /// </summary>
        public int CompactionCount
        {
            get => _CompactionCount;
            set => _CompactionCount = value;
        }

        #endregion
    }
}
