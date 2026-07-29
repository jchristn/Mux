namespace Mux.Core.Jobs
{
    /// <summary>
    /// Event emitted when a job reaches a terminal state.
    /// </summary>
    public class JobCompletedEvent : JobManagerEvent
    {
        #region Private-Members

        private JobState _FinalState = JobState.Completed;
        private string _ErrorMessage = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// The terminal state reached by the job.
        /// </summary>
        public JobState FinalState
        {
            get => _FinalState;
            set => _FinalState = value;
        }

        /// <summary>
        /// The failure message when the job failed, otherwise empty.
        /// </summary>
        public string ErrorMessage
        {
            get => _ErrorMessage;
            set => _ErrorMessage = value ?? string.Empty;
        }

        #endregion
    }
}
