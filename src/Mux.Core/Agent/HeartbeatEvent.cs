namespace Mux.Core.Agent
{
    using Mux.Core.Enums;

    /// <summary>
    /// Periodic heartbeat event indicating the agent loop is alive.
    /// </summary>
    public class HeartbeatEvent : AgentEvent
    {
        #region Private-Members

        private int _StepNumber = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="HeartbeatEvent"/> class.
        /// </summary>
        public HeartbeatEvent()
        {
            EventType = AgentEventTypeEnum.Heartbeat;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The current step number in the agent loop.
        /// </summary>
        public int StepNumber
        {
            get => _StepNumber;
            set => _StepNumber = value;
        }

        #endregion
    }
}
