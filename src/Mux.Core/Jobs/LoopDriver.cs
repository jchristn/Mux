namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Runs the loops of a <see cref="LoopScheduler"/> one iteration at a time until none can fire, for headless
    /// use (<c>mux print --loop</c>). Iterations never overlap: the next due loop starts only after the current
    /// iteration's turn returns. The wait between iterations is injectable so tests can advance a fake clock.
    /// </summary>
    public sealed class LoopDriver
    {
        #region Private-Members

        private readonly LoopScheduler _Scheduler;
        private readonly Func<TimeSpan, CancellationToken, Task> _Delay;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopDriver"/> class.
        /// </summary>
        /// <param name="scheduler">The scheduler whose loops to run. Must not be null.</param>
        /// <param name="delay">Waits for a duration; defaults to <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="scheduler"/> is null.</exception>
        public LoopDriver(LoopScheduler scheduler, Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            _Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _Delay = delay ?? Task.Delay;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Fires due loops until no loop is scheduled or running.
        /// </summary>
        /// <param name="runIteration">Runs one iteration: receives the loop and its iteration prompt, and returns true when the turn succeeded. A false result pauses the loop.</param>
        /// <param name="cancellationToken">Stops the driver; the running iteration is reported as failed.</param>
        /// <returns>The number of iterations run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runIteration"/> is null.</exception>
        public async Task<int> RunAsync(Func<LoopDefinition, string, CancellationToken, Task<bool>> runIteration, CancellationToken cancellationToken)
        {
            if (runIteration == null) throw new ArgumentNullException(nameof(runIteration));

            int iterations = 0;
            while (!cancellationToken.IsCancellationRequested && _Scheduler.HasLiveLoops())
            {
                IReadOnlyList<LoopDefinition> due = _Scheduler.GetDue();
                if (due.Count == 0)
                {
                    DateTime? next = _Scheduler.GetNextFireUtc();
                    if (!next.HasValue)
                    {
                        break;
                    }

                    TimeSpan wait = next.Value - _Scheduler.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        await _Delay(wait, cancellationToken).ConfigureAwait(false);
                    }

                    continue;
                }

                LoopDefinition? running = _Scheduler.TryBegin(due[0].Id);
                if (running == null)
                {
                    continue;
                }

                iterations++;
                bool succeeded = false;
                try
                {
                    succeeded = await runIteration(running, _Scheduler.BuildIterationPrompt(running), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _Scheduler.Complete(running.Id, failed: !succeeded);
                }
            }

            return iterations;
        }

        #endregion
    }
}
