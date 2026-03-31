namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Event indicating that a tool call has finished executing.
    /// </summary>
    public class ToolCallCompletedEvent : AgentEvent
    {
        #region Private-Members

        private string _ToolCallId = string.Empty;
        private string _ToolName = string.Empty;
        private ToolResult _Result = new ToolResult();
        private long _ElapsedMs = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolCallCompletedEvent"/> class.
        /// </summary>
        public ToolCallCompletedEvent()
        {
            EventType = AgentEventTypeEnum.ToolCallCompleted;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The identifier of the completed tool call.
        /// </summary>
        public string ToolCallId
        {
            get => _ToolCallId;
            set => _ToolCallId = value ?? throw new ArgumentNullException(nameof(ToolCallId));
        }

        /// <summary>
        /// The name of the tool that was executed.
        /// </summary>
        public string ToolName
        {
            get => _ToolName;
            set => _ToolName = value ?? string.Empty;
        }

        /// <summary>
        /// The result produced by the tool execution.
        /// </summary>
        public ToolResult Result
        {
            get => _Result;
            set => _Result = value ?? throw new ArgumentNullException(nameof(Result));
        }

        /// <summary>
        /// The elapsed time in milliseconds for the tool execution.
        /// </summary>
        public long ElapsedMs
        {
            get => _ElapsedMs;
            set => _ElapsedMs = value;
        }

        #endregion
    }
}
