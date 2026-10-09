namespace Mux.Core.Jobs
{
    /// <summary>
    /// The model's <c>schedule_next</c> decision for a running self-paced loop iteration: run again after a
    /// delay, or stop. Held by <see cref="LoopScheduler"/> until the iteration ends.
    /// </summary>
    public sealed class LoopDecision
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopDecision"/> class.
        /// </summary>
        /// <param name="stop">True to stop the loop.</param>
        /// <param name="delaySeconds">The delay before the next iteration, when not stopping.</param>
        /// <param name="reason">A one-line reason, or empty.</param>
        public LoopDecision(bool stop, int delaySeconds, string reason)
        {
            Stop = stop;
            DelaySeconds = delaySeconds;
            Reason = reason ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>Whether the loop should stop.</summary>
        public bool Stop { get; }

        /// <summary>The delay before the next iteration, in seconds.</summary>
        public int DelaySeconds { get; }

        /// <summary>The model's one-line reason, or empty.</summary>
        public string Reason { get; }

        #endregion
    }
}
