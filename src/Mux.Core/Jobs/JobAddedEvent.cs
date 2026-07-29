namespace Mux.Core.Jobs
{
    using System;

    /// <summary>
    /// Event emitted when a job is added to the manager.
    /// </summary>
    public class JobAddedEvent : JobManagerEvent
    {
        #region Private-Members

        private Job _Job;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="JobAddedEvent"/> class.
        /// </summary>
        /// <param name="job">The job that was added.</param>
        public JobAddedEvent(Job job)
        {
            _Job = job ?? throw new ArgumentNullException(nameof(job));
            JobId = job.Id;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The job that was added.
        /// </summary>
        public Job Job
        {
            get => _Job;
            set
            {
                _Job = value ?? throw new ArgumentNullException(nameof(Job));
                JobId = _Job.Id;
            }
        }

        #endregion
    }
}
