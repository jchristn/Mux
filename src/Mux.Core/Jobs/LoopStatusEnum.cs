namespace Mux.Core.Jobs
{
    /// <summary>
    /// The lifecycle state of a recurring prompt (a <c>/loop</c>).
    /// </summary>
    public enum LoopStatusEnum
    {
        /// <summary>
        /// The loop is waiting for its next fire time.
        /// </summary>
        Scheduled = 0,

        /// <summary>
        /// An iteration of the loop is running. A running loop is never fired again until the iteration ends.
        /// </summary>
        Running = 1,

        /// <summary>
        /// The loop is paused and does not fire until resumed. Loops restored with a session start paused.
        /// </summary>
        Paused = 2,

        /// <summary>
        /// The loop was stopped: cancelled by the user, stopped by the model, or ended without a
        /// <c>schedule_next</c> call in self-paced mode.
        /// </summary>
        Stopped = 3,

        /// <summary>
        /// The loop reached its iteration cap.
        /// </summary>
        Completed = 4
    }
}
