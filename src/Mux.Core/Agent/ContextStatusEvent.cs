namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event describing estimated context usage for the active conversation.
    /// </summary>
    public class ContextStatusEvent : AgentEvent
    {
        #region Private-Members

        private string _Scope = string.Empty;
        private int _EstimatedTokens = 0;
        private int _UsableInputLimit = 0;
        private int _RemainingTokens = 0;
        private double _RemainingPercent = 0;
        private int _WarningThresholdTokens = 0;
        private int _MessageCount = 0;
        private string _Trigger = string.Empty;
        private string _WarningLevel = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextStatusEvent"/> class.
        /// </summary>
        public ContextStatusEvent()
        {
            EventType = AgentEventTypeEnum.ContextStatus;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Scope of the measured conversation, such as session_history or active_conversation.
        /// </summary>
        public string Scope
        {
            get => _Scope;
            set => _Scope = value ?? string.Empty;
        }

        /// <summary>
        /// Estimated used input tokens for the measured scope.
        /// </summary>
        public int EstimatedTokens
        {
            get => _EstimatedTokens;
            set => _EstimatedTokens = value;
        }

        /// <summary>
        /// Estimated usable input budget after output reservation and safety margin.
        /// </summary>
        public int UsableInputLimit
        {
            get => _UsableInputLimit;
            set => _UsableInputLimit = value;
        }

        /// <summary>
        /// Estimated remaining tokens before reaching the usable input limit.
        /// </summary>
        public int RemainingTokens
        {
            get => _RemainingTokens;
            set => _RemainingTokens = value;
        }

        /// <summary>
        /// Remaining tokens as a percentage of the usable input limit.
        /// </summary>
        public double RemainingPercent
        {
            get => _RemainingPercent;
            set => _RemainingPercent = value;
        }

        /// <summary>
        /// Warning threshold in tokens for this scope.
        /// </summary>
        public int WarningThresholdTokens
        {
            get => _WarningThresholdTokens;
            set => _WarningThresholdTokens = value;
        }

        /// <summary>
        /// Number of conversation messages included in the estimate.
        /// </summary>
        public int MessageCount
        {
            get => _MessageCount;
            set => _MessageCount = value;
        }

        /// <summary>
        /// Trigger that caused this status event, such as preflight or post_compaction.
        /// </summary>
        public string Trigger
        {
            get => _Trigger;
            set => _Trigger = value ?? string.Empty;
        }

        /// <summary>
        /// Warning level such as ok, approaching, or critical.
        /// </summary>
        public string WarningLevel
        {
            get => _WarningLevel;
            set => _WarningLevel = value ?? string.Empty;
        }

        #endregion
    }
}
