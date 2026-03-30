namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event indicating that a proposed tool call was approved for execution.
    /// </summary>
    public class ToolCallApprovedEvent : AgentEvent
    {
        #region Private-Members

        private string _ToolCallId = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallApprovedEvent"/> class.
        /// </summary>
        public ToolCallApprovedEvent()
        {
            EventType = AgentEventTypeEnum.ToolCallApproved;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The identifier of the approved tool call.
        /// </summary>
        public string ToolCallId
        {
            get => _ToolCallId;
            set => _ToolCallId = value ?? throw new ArgumentNullException(nameof(ToolCallId));
        }

        #endregion
    }
}
