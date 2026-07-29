namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Pure scheduler that selects queued jobs that may start under a concurrency cap.
    /// </summary>
    public class JobScheduler
    {
        #region Public-Methods

        /// <summary>
        /// Selects queued jobs that may start without exceeding <paramref name="maxConcurrency"/>.
        /// </summary>
        /// <param name="jobs">The ordered job list to inspect.</param>
        /// <param name="maxConcurrency">The maximum number of active jobs. Values below one are treated as one.</param>
        /// <returns>The ordered list of queued jobs that may start now.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobs"/> is null.</exception>
        public IReadOnlyList<Job> SelectStartableJobs(IReadOnlyList<Job> jobs, int maxConcurrency)
        {
            if (jobs is null) throw new ArgumentNullException(nameof(jobs));

            int safeMaxConcurrency = Math.Max(1, maxConcurrency);
            int activeCount = CountActiveJobs(jobs);
            int availableSlots = safeMaxConcurrency - activeCount;
            List<Job> startableJobs = new List<Job>();

            if (availableSlots <= 0)
            {
                return startableJobs;
            }

            foreach (Job job in jobs)
            {
                if (job.State != JobState.Queued)
                {
                    continue;
                }

                startableJobs.Add(job);
                availableSlots--;

                if (availableSlots == 0)
                {
                    break;
                }
            }

            return startableJobs;
        }

        /// <summary>
        /// Counts jobs that currently occupy an execution slot.
        /// </summary>
        /// <param name="jobs">The jobs to inspect.</param>
        /// <returns>The number of active jobs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobs"/> is null.</exception>
        public int CountActiveJobs(IReadOnlyList<Job> jobs)
        {
            if (jobs is null) throw new ArgumentNullException(nameof(jobs));

            int activeCount = 0;
            foreach (Job job in jobs)
            {
                if (IsActiveState(job.State))
                {
                    activeCount++;
                }
            }

            return activeCount;
        }

        /// <summary>
        /// Determines whether a state consumes an execution slot.
        /// </summary>
        /// <param name="state">The state to inspect.</param>
        /// <returns>True when the state is active; otherwise false.</returns>
        public static bool IsActiveState(JobState state)
        {
            return state == JobState.Running
                || state == JobState.AwaitingApproval
                || state == JobState.AwaitingWriteLease;
        }

        #endregion
    }
}
