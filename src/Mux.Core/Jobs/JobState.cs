namespace Mux.Core.Jobs
{
    /// <summary>
    /// Represents the lifecycle state of a mux job.
    /// </summary>
    public enum JobState
    {
        /// <summary>
        /// The job is waiting for an execution slot.
        /// </summary>
        Queued = 0,

        /// <summary>
        /// The job is actively running an agent turn.
        /// </summary>
        Running = 1,

        /// <summary>
        /// The job is blocked waiting for tool approval.
        /// </summary>
        AwaitingApproval = 2,

        /// <summary>
        /// The job is blocked waiting for the workspace write lease.
        /// </summary>
        AwaitingWriteLease = 3,

        /// <summary>
        /// The job has been paused before starting or between turns.
        /// </summary>
        Paused = 4,

        /// <summary>
        /// The job completed successfully.
        /// </summary>
        Completed = 5,

        /// <summary>
        /// The job failed with an unhandled error.
        /// </summary>
        Failed = 6,

        /// <summary>
        /// The job was cancelled.
        /// </summary>
        Cancelled = 7
    }
}
