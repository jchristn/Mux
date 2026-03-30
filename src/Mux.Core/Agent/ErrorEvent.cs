namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Event representing an error that occurred during agent processing.
    /// </summary>
    public class ErrorEvent : AgentEvent
    {
        #region Private-Members

        private string _Code = string.Empty;
        private string _Message = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorEvent"/> class.
        /// </summary>
        public ErrorEvent()
        {
            EventType = AgentEventTypeEnum.Error;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// A stable machine-readable error code (e.g. "llm_error", "tool_timeout").
        /// </summary>
        public string Code
        {
            get => _Code;
            set => _Code = value ?? throw new ArgumentNullException(nameof(Code));
        }

        /// <summary>
        /// A human-readable description of the error.
        /// </summary>
        public string Message
        {
            get => _Message;
            set => _Message = value ?? throw new ArgumentNullException(nameof(Message));
        }

        #endregion
    }
}
