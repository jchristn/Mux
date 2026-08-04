namespace Mux.Core.Tasks
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;

    /// <summary>
    /// Drives a task plan to completion by dispatching dependency-ready tasks as their own jobs through a
    /// <see cref="JobManager"/>. The DAG's edges become the schedule: independent tasks fan out (up to the
    /// manager's <see cref="JobManager.MaxConcurrency"/>, with the shared workspace write lease serializing
    /// their mutating tool calls), while chains run in order as each dependency completes. Each child job's
    /// terminal state is folded back into its task, which unblocks dependents.
    /// <para>
    /// This is the opt-in orchestration engine gated behind <c>taskParallelismEnabled</c>. It reuses the job
    /// manager's scheduler and write lease rather than introducing a second concurrency primitive.
    /// </para>
    /// </summary>
    public sealed class TaskOrchestrator
    {
        #region Private-Members

        private readonly TaskPlan _Plan;
        private readonly JobManager _JobManager;
        private readonly object _SyncRoot = new object();
        private readonly Dictionary<string, string> _JobToTask = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly TimeSpan _WaitTimeout = TimeSpan.FromSeconds(30);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TaskOrchestrator"/> class.
        /// </summary>
        /// <param name="plan">The task plan to execute. Must not be null.</param>
        /// <param name="jobManager">The job manager that runs each task as a job. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public TaskOrchestrator(TaskPlan plan, JobManager jobManager)
        {
            _Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _JobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Runs the plan to completion: repeatedly dispatches ready tasks as jobs and folds each finished
        /// job's terminal state back into its task until every task is terminal or no progress is possible
        /// (for example a failed dependency permanently blocks its dependents).
        /// </summary>
        /// <param name="cancellationToken">A token to cancel orchestration. Cancellation also cancels the
        /// child jobs this orchestrator dispatched.</param>
        /// <returns>A task that completes when orchestration finishes.</returns>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken"/> is
        /// cancelled.</exception>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            SemaphoreSlim signal = new SemaphoreSlim(0);
            EventHandler<JobManagerEvent> handler = (object? sender, JobManagerEvent managerEvent) =>
            {
                if (managerEvent is JobCompletedEvent)
                {
                    signal.Release();
                }
            };

            _JobManager.EventPublished += handler;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await DispatchReadyAsync(cancellationToken).ConfigureAwait(false);
                    Reconcile();

                    if (AllTerminal())
                    {
                        break;
                    }

                    if (InFlightCount() == 0 && _Plan.ReadyTasks().Count == 0)
                    {
                        // Nothing running and nothing ready to dispatch: a failed or blocked dependency has
                        // stalled the remaining tasks. Stop rather than wait forever. The ready check is
                        // essential — when a dependency's job completes fast enough to be reconciled in the
                        // same iteration it was dispatched, in-flight can momentarily hit zero while a
                        // now-ready dependent is still pending; breaking here would orphan it.
                        break;
                    }

                    await signal.WaitAsync(_WaitTimeout, cancellationToken).ConfigureAwait(false);
                    Reconcile();
                }
            }
            catch (OperationCanceledException)
            {
                await CancelDispatchedJobsAsync().ConfigureAwait(false);
                throw;
            }
            finally
            {
                _JobManager.EventPublished -= handler;
                signal.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private async Task DispatchReadyAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<AgentTask> ready = _Plan.ReadyTasks();
            foreach (AgentTask task in ready)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Mark in progress before submitting so the next readiness pass does not redispatch it.
                _Plan.TryUpdateTask(task.Id, AgentTaskStatusEnum.InProgress, null, out _);

                Job job = await _JobManager.SubmitAsync(task.Title, cancellationToken).ConfigureAwait(false);
                lock (_SyncRoot)
                {
                    _JobToTask[job.Id] = task.Id;
                }
            }
        }

        private void Reconcile()
        {
            List<KeyValuePair<string, string>> mappings;
            lock (_SyncRoot)
            {
                mappings = new List<KeyValuePair<string, string>>(_JobToTask);
            }

            foreach (KeyValuePair<string, string> mapping in mappings)
            {
                Job? job = _JobManager.GetJob(mapping.Key);
                if (job == null)
                {
                    continue;
                }

                JobState state = job.State;
                if (state == JobState.Completed)
                {
                    _Plan.TryUpdateTask(mapping.Value, AgentTaskStatusEnum.Completed, null, out _);
                    Forget(mapping.Key);
                }
                else if (state == JobState.Failed || state == JobState.Cancelled)
                {
                    _Plan.TryUpdateTask(mapping.Value, AgentTaskStatusEnum.Failed, "Task job " + mapping.Key + " ended in state " + state + ".", out _);
                    Forget(mapping.Key);
                }
            }
        }

        private void Forget(string jobId)
        {
            lock (_SyncRoot)
            {
                _JobToTask.Remove(jobId);
            }
        }

        private async Task CancelDispatchedJobsAsync()
        {
            List<string> jobIds;
            lock (_SyncRoot)
            {
                jobIds = new List<string>(_JobToTask.Keys);
            }

            foreach (string jobId in jobIds)
            {
                try
                {
                    await _JobManager.CancelAsync(jobId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    // The job already reached a terminal state between reconcile and cancel; ignore.
                }
            }
        }

        private bool AllTerminal()
        {
            foreach (AgentTask task in _Plan.Snapshot())
            {
                if (task.Status != AgentTaskStatusEnum.Completed
                    && task.Status != AgentTaskStatusEnum.Failed
                    && task.Status != AgentTaskStatusEnum.Skipped)
                {
                    return false;
                }
            }

            return true;
        }

        private int InFlightCount()
        {
            int count = 0;
            foreach (AgentTask task in _Plan.Snapshot())
            {
                if (task.Status == AgentTaskStatusEnum.InProgress)
                {
                    count++;
                }
            }

            return count;
        }

        #endregion
    }
}
