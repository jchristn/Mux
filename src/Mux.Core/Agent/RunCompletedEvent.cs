namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;
    using Mux.Core.Tasks;

    /// <summary>
    /// Event emitted when a mux run completes with a final status summary.
    /// </summary>
    public class RunCompletedEvent : AgentEvent
    {
        #region Private-Members

        private string _RunId = string.Empty;
        private string _SessionId = string.Empty;
        private string _Status = string.Empty;
        private int _IterationsCompleted = 0;
        private int _ToolCallCount = 0;
        private int _ErrorCount = 0;
        private int _AssistantTextChars = 0;
        private long _DurationMs = 0;
        private int _FinalEstimatedTokens = 0;
        private int _CompactionCount = 0;
        private int _InputTokens = 0;
        private int _OutputTokens = 0;
        private int _TotalTokens = 0;

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
        /// Persisted session identifier this run belongs to, or empty when the run is not associated with
        /// a persisted session.
        /// </summary>
        public string SessionId
        {
            get => _SessionId;
            set => _SessionId = value ?? string.Empty;
        }

        /// <summary>
        /// Final run status such as completed, completed_with_errors, max_iterations_reached, or
        /// budget_exceeded.
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

        /// <summary>
        /// Provider-reported prompt/input tokens across the run (0 when the provider reported none).
        /// </summary>
        public int InputTokens
        {
            get => _InputTokens;
            set => _InputTokens = value;
        }

        /// <summary>
        /// Provider-reported completion/output tokens across the run (0 when the provider reported none).
        /// </summary>
        public int OutputTokens
        {
            get => _OutputTokens;
            set => _OutputTokens = value;
        }

        /// <summary>
        /// Provider-reported total tokens across the run (0 when the provider reported none).
        /// </summary>
        public int TotalTokens
        {
            get => _TotalTokens;
            set => _TotalTokens = value;
        }

        /// <summary>
        /// A tally of the job's task plan by status at run end, or null when the job had no task plan.
        /// </summary>
        public TaskPlanSummary? TaskSummary { get; set; }

        #endregion
    }
}
