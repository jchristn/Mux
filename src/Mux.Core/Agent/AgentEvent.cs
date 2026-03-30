namespace Mux.Core.Agent
{
    using System;
    using Mux.Core.Enums;

    /// <summary>
    /// Abstract base class for all events emitted by the agent loop.
    /// </summary>
    public abstract class AgentEvent
    {
        #region Private-Members

        private DateTime _TimestampUtc = DateTime.UtcNow;
        private AgentEventTypeEnum _EventType;

        #endregion

        #region Public-Members

        /// <summary>
        /// The UTC timestamp when this event was created.
        /// </summary>
        public DateTime TimestampUtc
        {
            get => _TimestampUtc;
            set => _TimestampUtc = value;
        }

        /// <summary>
        /// The type of this agent event.
        /// </summary>
        public AgentEventTypeEnum EventType
        {
            get => _EventType;
            set => _EventType = value;
        }

        #endregion
    }
}
