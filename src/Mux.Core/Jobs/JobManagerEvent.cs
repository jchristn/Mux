namespace Mux.Core.Jobs
{
    using System;

    /// <summary>
    /// Base type for notifications emitted by <see cref="JobManager"/>.
    /// </summary>
    public abstract class JobManagerEvent
    {
        #region Private-Members

        private DateTime _TimestampUtc = DateTime.UtcNow;
        private string _JobId = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// The UTC timestamp when this manager event was created.
        /// </summary>
        public DateTime TimestampUtc
        {
            get => _TimestampUtc;
            set => _TimestampUtc = value;
        }

        /// <summary>
        /// The job identifier related to this event.
        /// </summary>
        public string JobId
        {
            get => _JobId;
            set => _JobId = value ?? string.Empty;
        }

        #endregion
    }
}
