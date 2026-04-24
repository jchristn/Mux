namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event emitted when mux compacts conversation state to reduce context usage.
    /// </summary>
    public class ContextCompactedEvent : AgentEvent
    {
        #region Private-Members

        private string _Scope = string.Empty;
        private string _Mode = string.Empty;
        private string _Strategy = string.Empty;
        private int _MessagesBefore = 0;
        private int _MessagesAfter = 0;
        private int _EstimatedTokensBefore = 0;
        private int _EstimatedTokensAfter = 0;
        private bool _SummaryCreated = false;
        private string _Reason = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ContextCompactedEvent"/> class.
        /// </summary>
        public ContextCompactedEvent()
        {
            EventType = AgentEventTypeEnum.ContextCompacted;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Scope of the compacted conversation, such as active_conversation.
        /// </summary>
        public string Scope
        {
            get => _Scope;
            set => _Scope = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the compaction was automatic or manual.
        /// </summary>
        public string Mode
        {
            get => _Mode;
            set => _Mode = value ?? string.Empty;
        }

        /// <summary>
        /// Strategy name, such as trim or summary_then_trim.
        /// </summary>
        public string Strategy
        {
            get => _Strategy;
            set => _Strategy = value ?? string.Empty;
        }

        /// <summary>
        /// Message count before compaction.
        /// </summary>
        public int MessagesBefore
        {
            get => _MessagesBefore;
            set => _MessagesBefore = value;
        }

        /// <summary>
        /// Message count after compaction.
        /// </summary>
        public int MessagesAfter
        {
            get => _MessagesAfter;
            set => _MessagesAfter = value;
        }

        /// <summary>
        /// Estimated used tokens before compaction.
        /// </summary>
        public int EstimatedTokensBefore
        {
            get => _EstimatedTokensBefore;
            set => _EstimatedTokensBefore = value;
        }

        /// <summary>
        /// Estimated used tokens after compaction.
        /// </summary>
        public int EstimatedTokensAfter
        {
            get => _EstimatedTokensAfter;
            set => _EstimatedTokensAfter = value;
        }

        /// <summary>
        /// Whether the compaction created a synthetic summary message.
        /// </summary>
        public bool SummaryCreated
        {
            get => _SummaryCreated;
            set => _SummaryCreated = value;
        }

        /// <summary>
        /// Human-readable reason for compaction.
        /// </summary>
        public string Reason
        {
            get => _Reason;
            set => _Reason = value ?? string.Empty;
        }

        #endregion
    }
}
