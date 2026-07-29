namespace Mux.Core.Jobs
{
    /// <summary>
    /// Event emitted when a job changes lifecycle state.
    /// </summary>
    public class JobStateChangedEvent : JobManagerEvent
    {
        #region Private-Members

        private JobState _PreviousState = JobState.Queued;
        private JobState _CurrentState = JobState.Queued;

        #endregion

        #region Public-Members

        /// <summary>
        /// The state held by the job before the transition.
        /// </summary>
        public JobState PreviousState
        {
            get => _PreviousState;
            set => _PreviousState = value;
        }

        /// <summary>
        /// The state held by the job after the transition.
        /// </summary>
        public JobState CurrentState
        {
            get => _CurrentState;
            set => _CurrentState = value;
        }

        #endregion
    }
}
