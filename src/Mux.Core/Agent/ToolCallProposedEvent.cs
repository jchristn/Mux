namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Event indicating that the model has proposed a tool call.
    /// </summary>
    public class ToolCallProposedEvent : AgentEvent
    {
        #region Private-Members

        private ToolCall _ToolCall = new ToolCall();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallProposedEvent"/> class.
        /// </summary>
        public ToolCallProposedEvent()
        {
            EventType = AgentEventTypeEnum.ToolCallProposed;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The tool call proposed by the model.
        /// </summary>
        public ToolCall ToolCall
        {
            get => _ToolCall;
            set => _ToolCall = value ?? throw new ArgumentNullException(nameof(ToolCall));
        }

        #endregion
    }
}
