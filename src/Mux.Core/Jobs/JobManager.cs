namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Owns the live set of jobs, schedules queued work under a concurrency cap, and pumps each
    /// job's agent events into an isolated per-job channel.
    /// </summary>
    public class JobManager : IAsyncDisposable
    {
        #region Private-Members

        private readonly object _SyncRoot = new object();
        private readonly List<Job> _Jobs = new List<Job>();
        private readonly Dictionary<string, Task> _Workers = new Dictionary<string, Task>();
        private readonly JobScheduler _Scheduler = new JobScheduler();
        private readonly WriteLease _WriteLease;
        private readonly Channel<JobManagerEvent> _EventChannel = Channel.CreateUnbounded<JobManagerEvent>();
        private readonly CancellationTokenSource _ShutdownTokenSource = new CancellationTokenSource();
        private readonly Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> _AgentRunner;
        private int _MaxConcurrency = 3;
        private int _NextJobNumber = 1;
        private string _SessionId;
        private string? _FocusedJobId = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="JobManager"/> class.
        /// </summary>
        /// <param name="agentRunner">The function that executes one agent turn for a job and prompt.</param>
        /// <param name="maxConcurrency">The maximum number of active jobs. Values below one are treated as one.</param>
        /// <param name="sessionId">Optional owning session id. A new id is generated when omitted.</param>
        /// <param name="writeLease">Optional shared workspace write lease. A new lease is created when omitted.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentRunner"/> is null.</exception>
        public JobManager(
            Func<Job, string, CancellationToken, IAsyncEnumerable<AgentEvent>> agentRunner,
            int maxConcurrency = 3,
            string? sessionId = null,
            WriteLease? writeLease = null)
        {
            _AgentRunner = agentRunner ?? throw new ArgumentNullException(nameof(agentRunner));
            _MaxConcurrency = Math.Max(1, maxConcurrency);
            _SessionId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
            _WriteLease = writeLease ?? new WriteLease();
        }

        /// <summary>
        /// Creates a manager that constructs a fresh <see cref="AgentLoop"/> for each job turn.
        /// </summary>
        /// <param name="templateOptions">The baseline agent-loop options to clone for each job.</param>
        /// <param name="maxConcurrency">The maximum number of active jobs.</param>
        /// <param name="sessionId">Optional owning session id.</param>
        /// <returns>A configured <see cref="JobManager"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="templateOptions"/> is null.</exception>
        public static JobManager CreateForAgentLoop(
            AgentLoopOptions templateOptions,
            int maxConcurrency = 3,
            string? sessionId = null)
        {
            if (templateOptions is null) throw new ArgumentNullException(nameof(templateOptions));

            WriteLease lease = new WriteLease();
            return new JobManager(
                (Job job, string prompt, CancellationToken cancellationToken) =>
                {
                    AgentLoopOptions options = CloneOptionsForJob(templateOptions, job);
                    options.WriteLease = lease;
                    options.JobId = job.Id;
                    options.OnWriteLeaseWaitChanged = (bool waiting) => ApplyLeaseWaitState(job, waiting);
                    return RunAgentLoopAsync(options, prompt, cancellationToken);
                },
                maxConcurrency,
                sessionId,
                lease);
        }

        #endregion

        #region Public-Events

        /// <summary>
        /// Raised whenever a manager event is published.
        /// </summary>
        public event EventHandler<JobManagerEvent>? EventPublished;

        #endregion

        #region Public-Members

        /// <summary>
        /// The configured maximum number of active jobs. Values below one are treated as one.
        /// </summary>
        public int MaxConcurrency
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _MaxConcurrency;
                }
            }
            set
            {
                lock (_SyncRoot)
                {
                    _MaxConcurrency = Math.Max(1, value);
                }

                StartEligibleJobs();
            }
        }

        /// <summary>
        /// The owning session identifier for jobs created by this manager.
        /// </summary>
        public string SessionId
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _SessionId;
                }
            }
        }

        /// <summary>
        /// The shared workspace write lease that serializes mutating tools across this manager's jobs
        /// while letting read-only tools run concurrently.
        /// </summary>
        public WriteLease WriteLease
        {
            get => _WriteLease;
        }

        /// <summary>
        /// Snapshot of all jobs known to the manager in scheduling order.
        /// </summary>
        public IReadOnlyList<Job> Jobs
        {
            get
            {
                lock (_SyncRoot)
                {
                    return new List<Job>(_Jobs);
                }
            }
        }

        /// <summary>
        /// The currently focused job, or null when no job has focus.
        /// </summary>
        public Job? FocusedJob
        {
            get
            {
                lock (_SyncRoot)
                {
                    return FindJobNoLock(_FocusedJobId);
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reads manager events until the manager is disposed.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the read.</param>
        /// <returns>An async sequence of manager events.</returns>
        public IAsyncEnumerable<JobManagerEvent> ReadEventsAsync(CancellationToken cancellationToken)
        {
            return _EventChannel.Reader.ReadAllAsync(cancellationToken);
        }

        /// <summary>
        /// Submits a prompt using the default approval policy and focused job history.
        /// </summary>
        /// <param name="prompt">The prompt to submit.</param>
        /// <param name="cancellationToken">A token to observe before enqueueing.</param>
        /// <returns>The created job.</returns>
        public Task<Job> SubmitAsync(string prompt, CancellationToken cancellationToken)
        {
            return SubmitAsync(prompt, ApprovalPolicyEnum.Ask, null, cancellationToken);
        }

        /// <summary>
        /// Submits a prompt and starts it immediately when a concurrency slot is available.
        /// </summary>
        /// <param name="prompt">The prompt to submit.</param>
        /// <param name="approvalPolicy">The per-job approval policy.</param>
        /// <param name="conversationHistory">Optional history to fork; focused history is used when null.</param>
        /// <param name="cancellationToken">A token to observe before enqueueing.</param>
        /// <returns>The created job.</returns>
        public Task<Job> SubmitAsync(
            string prompt,
            ApprovalPolicyEnum approvalPolicy,
            IEnumerable<ConversationMessage>? conversationHistory,
            CancellationToken cancellationToken)
        {
            return AddJobAsync(prompt, approvalPolicy, conversationHistory, cancellationToken);
        }

        /// <summary>
        /// Enqueues a prompt and starts it when the scheduler allows.
        /// </summary>
        /// <param name="prompt">The prompt to enqueue.</param>
        /// <param name="approvalPolicy">The per-job approval policy.</param>
        /// <param name="conversationHistory">Optional history to fork; focused history is used when null.</param>
        /// <param name="cancellationToken">A token to observe before enqueueing.</param>
        /// <returns>The created job.</returns>
        public Task<Job> EnqueueAsync(
            string prompt,
            ApprovalPolicyEnum approvalPolicy,
            IEnumerable<ConversationMessage>? conversationHistory,
            CancellationToken cancellationToken)
        {
            return AddJobAsync(prompt, approvalPolicy, conversationHistory, cancellationToken);
        }

        /// <summary>
        /// Appends a follow-up prompt to an existing non-terminal job.
        /// </summary>
        /// <param name="jobId">The target job id.</param>
        /// <param name="prompt">The follow-up prompt.</param>
        /// <param name="cancellationToken">A token to observe before enqueueing.</param>
        /// <returns>True when the follow-up was added; false when the job was not found.</returns>
        public Task<bool> AddFollowUpAsync(string jobId, string prompt, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            bool added = false;
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                Job? job = FindJobNoLock(jobId);
                if (job == null)
                {
                    return Task.FromResult(false);
                }

                job.AddFollowUp(prompt);
                added = true;
            }

            if (added)
            {
                StartEligibleJobs();
            }

            return Task.FromResult(added);
        }

        /// <summary>
        /// Cancels a job if it exists and is not terminal.
        /// </summary>
        /// <param name="jobId">The target job id.</param>
        /// <param name="cancellationToken">A token to observe before cancellation.</param>
        /// <returns>True when cancellation was requested; false when the job was missing or terminal.</returns>
        public Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            Job? job = null;
            JobState previousState = JobState.Queued;
            bool completeImmediately = false;
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                job = FindJobNoLock(jobId);
                if (job == null || job.IsTerminal)
                {
                    return Task.FromResult(false);
                }

                previousState = job.SetState(JobState.Cancelled);
                job.CancellationTokenSource.Cancel();
                completeImmediately = previousState == JobState.Queued || previousState == JobState.Paused;
            }

            PublishStateChanged(job, previousState, JobState.Cancelled);

            if (completeImmediately)
            {
                job.EventWriter.TryComplete();
                PublishCompleted(job, JobState.Cancelled, string.Empty);
                StartEligibleJobs();
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// Cancels every non-terminal job.
        /// </summary>
        /// <param name="cancellationToken">A token to observe before cancellation.</param>
        /// <returns>A task that completes after cancellation has been requested.</returns>
        public async Task CancelAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<string> jobIds = new List<string>();
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                foreach (Job job in _Jobs)
                {
                    if (!job.IsTerminal)
                    {
                        jobIds.Add(job.Id);
                    }
                }
            }

            foreach (string jobId in jobIds)
            {
                await CancelAsync(jobId, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Pauses a queued job so the scheduler will skip it.
        /// </summary>
        /// <param name="jobId">The target job id.</param>
        /// <param name="cancellationToken">A token to observe before pausing.</param>
        /// <returns>True when the job was paused; otherwise false.</returns>
        public Task<bool> PauseAsync(string jobId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            Job? job = null;
            JobState previousState = JobState.Queued;
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                job = FindJobNoLock(jobId);
                if (job == null || job.State != JobState.Queued)
                {
                    return Task.FromResult(false);
                }

                previousState = job.SetState(JobState.Paused);
            }

            PublishStateChanged(job, previousState, JobState.Paused);
            return Task.FromResult(true);
        }

        /// <summary>
        /// Resumes a paused job by returning it to the queued state.
        /// </summary>
        /// <param name="jobId">The target job id.</param>
        /// <param name="cancellationToken">A token to observe before resuming.</param>
        /// <returns>True when the job was resumed; otherwise false.</returns>
        public Task<bool> ResumeAsync(string jobId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            Job? job = null;
            JobState previousState = JobState.Paused;
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                job = FindJobNoLock(jobId);
                if (job == null || job.State != JobState.Paused)
                {
                    return Task.FromResult(false);
                }

                previousState = job.SetState(JobState.Queued);
            }

            PublishStateChanged(job, previousState, JobState.Queued);
            StartEligibleJobs();
            return Task.FromResult(true);
        }

        /// <summary>
        /// Reorders a job in the scheduler list.
        /// </summary>
        /// <param name="jobId">The target job id.</param>
        /// <param name="newIndex">The zero-based destination index in the full job list.</param>
        /// <param name="cancellationToken">A token to observe before reordering.</param>
        /// <returns>True when the job was moved; otherwise false.</returns>
        public Task<bool> ReorderAsync(string jobId, int newIndex, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            bool moved = false;
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                int currentIndex = -1;
                for (int i = 0; i < _Jobs.Count; i++)
                {
                    if (string.Equals(_Jobs[i].Id, jobId, StringComparison.Ordinal))
                    {
                        currentIndex = i;
                        break;
                    }
                }

                if (currentIndex < 0)
                {
                    return Task.FromResult(false);
                }

                int boundedIndex = Math.Clamp(newIndex, 0, _Jobs.Count - 1);
                if (currentIndex == boundedIndex)
                {
                    return Task.FromResult(true);
                }

                Job job = _Jobs[currentIndex];
                _Jobs.RemoveAt(currentIndex);
                _Jobs.Insert(boundedIndex, job);
                moved = true;
            }

            if (moved)
            {
                StartEligibleJobs();
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// Focuses an existing job.
        /// </summary>
        /// <param name="jobId">The target job id.</param>
        /// <returns>True when focus changed; false when the job was not found.</returns>
        public bool Focus(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                Job? job = FindJobNoLock(jobId);
                if (job == null)
                {
                    return false;
                }

                _FocusedJobId = job.Id;
                return true;
            }
        }

        /// <summary>
        /// Gets a job by id.
        /// </summary>
        /// <param name="jobId">The job id.</param>
        /// <returns>The matching job, or null.</returns>
        public Job? GetJob(string jobId)
        {
            if (string.IsNullOrWhiteSpace(jobId)) throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            lock (_SyncRoot)
            {
                return FindJobNoLock(jobId);
            }
        }

        /// <summary>
        /// Disposes the manager, cancelling any non-terminal jobs.
        /// </summary>
        /// <returns>A task that completes when active workers have observed cancellation.</returns>
        public async ValueTask DisposeAsync()
        {
            List<Task> workers = new List<Task>();
            List<JobStateChangedEvent> stateEvents = new List<JobStateChangedEvent>();
            List<JobCompletedEvent> completedEvents = new List<JobCompletedEvent>();

            lock (_SyncRoot)
            {
                if (_Disposed)
                {
                    return;
                }

                _Disposed = true;
                _ShutdownTokenSource.Cancel();

                foreach (Job job in _Jobs)
                {
                    if (!job.IsTerminal)
                    {
                        JobState previousState = job.SetState(JobState.Cancelled);
                        job.CancellationTokenSource.Cancel();
                        job.EventWriter.TryComplete();
                        stateEvents.Add(CreateStateChanged(job, previousState, JobState.Cancelled));
                        completedEvents.Add(CreateCompleted(job, JobState.Cancelled, string.Empty));
                    }
                }

                foreach (Task worker in _Workers.Values)
                {
                    workers.Add(worker);
                }
            }

            foreach (JobStateChangedEvent stateEvent in stateEvents)
            {
                Publish(stateEvent);
            }

            foreach (JobCompletedEvent completedEvent in completedEvents)
            {
                Publish(completedEvent);
            }

            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }

            _EventChannel.Writer.TryComplete();
            _ShutdownTokenSource.Dispose();
        }

        #endregion

        #region Private-Methods

        private Task<Job> AddJobAsync(
            string prompt,
            ApprovalPolicyEnum approvalPolicy,
            IEnumerable<ConversationMessage>? conversationHistory,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt cannot be null or empty.", nameof(prompt));

            Job job;
            lock (_SyncRoot)
            {
                ThrowIfDisposedNoLock();
                string id = "j" + _NextJobNumber.ToString();
                _NextJobNumber++;

                IEnumerable<ConversationMessage>? effectiveHistory = conversationHistory ?? GetFocusedHistoryNoLock();
                job = new Job(
                    id,
                    _SessionId,
                    CreateTitle(prompt),
                    prompt,
                    approvalPolicy,
                    effectiveHistory);

                _Jobs.Add(job);
                if (_FocusedJobId == null)
                {
                    _FocusedJobId = job.Id;
                }
            }

            Publish(new JobAddedEvent(job));
            StartEligibleJobs();
            return Task.FromResult(job);
        }

        private void StartEligibleJobs()
        {
            List<JobStateChangedEvent> stateEvents = new List<JobStateChangedEvent>();

            lock (_SyncRoot)
            {
                if (_Disposed)
                {
                    return;
                }

                IReadOnlyList<Job> startableJobs = _Scheduler.SelectStartableJobs(_Jobs, _MaxConcurrency);
                foreach (Job job in startableJobs)
                {
                    JobState previousState = job.SetState(JobState.Running);
                    stateEvents.Add(CreateStateChanged(job, previousState, JobState.Running));
                    _Workers[job.Id] = Task.Run(() => RunJobAsync(job, _ShutdownTokenSource.Token));
                }
            }

            foreach (JobStateChangedEvent stateEvent in stateEvents)
            {
                Publish(stateEvent);
            }
        }

        private async Task RunJobAsync(Job job, CancellationToken managerCancellationToken)
        {
            using (CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                managerCancellationToken,
                job.CancellationToken))
            {
                try
                {
                    while (true)
                    {
                        linkedTokenSource.Token.ThrowIfCancellationRequested();
                        string? prompt = job.DequeuePromptForRun();
                        if (prompt == null)
                        {
                            break;
                        }

                        await foreach (AgentEvent agentEvent in _AgentRunner(job, prompt, linkedTokenSource.Token).ConfigureAwait(false))
                        {
                            linkedTokenSource.Token.ThrowIfCancellationRequested();
                            job.RecordEvent(agentEvent);
                            await job.EventWriter.WriteAsync(agentEvent, linkedTokenSource.Token).ConfigureAwait(false);
                        }
                    }

                    CompleteWorkerJob(job, JobState.Completed, string.Empty);
                }
                catch (OperationCanceledException)
                {
                    CompleteWorkerJob(job, JobState.Cancelled, string.Empty);
                }
                catch (Exception exception)
                {
                    job.RecordFailure(exception);
                    ErrorEvent errorEvent = new ErrorEvent
                    {
                        Code = "job_failed",
                        Message = exception.Message,
                        FailureCategory = "runtime"
                    };
                    job.RecordEvent(errorEvent);
                    job.EventWriter.TryWrite(errorEvent);
                    CompleteWorkerJob(job, JobState.Failed, exception.Message);
                }
                finally
                {
                    job.EventWriter.TryComplete();
                    lock (_SyncRoot)
                    {
                        _Workers.Remove(job.Id);
                    }

                    StartEligibleJobs();
                }
            }
        }

        private void CompleteWorkerJob(Job job, JobState finalState, string errorMessage)
        {
            JobState previousState = job.SetState(finalState);
            PublishStateChanged(job, previousState, finalState);
            PublishCompleted(job, finalState, errorMessage);
        }

        private void PublishStateChanged(Job job, JobState previousState, JobState currentState)
        {
            if (previousState == currentState)
            {
                return;
            }

            Publish(CreateStateChanged(job, previousState, currentState));
        }

        private void PublishCompleted(Job job, JobState finalState, string errorMessage)
        {
            Publish(CreateCompleted(job, finalState, errorMessage));
        }

        private void Publish(JobManagerEvent managerEvent)
        {
            _EventChannel.Writer.TryWrite(managerEvent);
            EventPublished?.Invoke(this, managerEvent);
        }

        private Job? FindJobNoLock(string? jobId)
        {
            if (jobId == null)
            {
                return null;
            }

            foreach (Job job in _Jobs)
            {
                if (string.Equals(job.Id, jobId, StringComparison.Ordinal))
                {
                    return job;
                }
            }

            return null;
        }

        private List<ConversationMessage>? GetFocusedHistoryNoLock()
        {
            Job? focusedJob = FindJobNoLock(_FocusedJobId);
            return focusedJob?.ConversationHistory;
        }

        private void ThrowIfDisposedNoLock()
        {
            if (_Disposed)
            {
                throw new ObjectDisposedException(nameof(JobManager));
            }
        }

        private static JobStateChangedEvent CreateStateChanged(Job job, JobState previousState, JobState currentState)
        {
            return new JobStateChangedEvent
            {
                JobId = job.Id,
                PreviousState = previousState,
                CurrentState = currentState
            };
        }

        private static JobCompletedEvent CreateCompleted(Job job, JobState finalState, string errorMessage)
        {
            return new JobCompletedEvent
            {
                JobId = job.Id,
                FinalState = finalState,
                ErrorMessage = errorMessage
            };
        }

        private static void ApplyLeaseWaitState(Job job, bool waiting)
        {
            // Conditional transitions so a lease-wait flip can never clobber a concurrent
            // cancellation/terminal state. Not published as a manager event; the transition is
            // observable via Job.State and the lease's holder/waiter lists.
            if (waiting)
            {
                job.TrySetAwaitingWriteLease();
            }
            else
            {
                job.TryResumeRunningFromLeaseWait();
            }
        }

        private static string CreateTitle(string prompt)
        {
            string trimmed = prompt.Trim();
            int newlineIndex = trimmed.IndexOfAny(new[] { '\r', '\n' });
            if (newlineIndex >= 0)
            {
                trimmed = trimmed.Substring(0, newlineIndex);
            }

            if (trimmed.Length <= 60)
            {
                return trimmed;
            }

            return trimmed.Substring(0, 57) + "...";
        }

        private static AgentLoopOptions CloneOptionsForJob(AgentLoopOptions templateOptions, Job job)
        {
            AgentLoopOptions options = new AgentLoopOptions(templateOptions.Endpoint)
            {
                ConversationHistory = job.ConversationHistory,
                SystemPrompt = templateOptions.SystemPrompt,
                ApprovalPolicy = job.ApprovalPolicy,
                WorkingDirectory = templateOptions.WorkingDirectory,
                MaxIterations = templateOptions.MaxIterations,
                Verbose = templateOptions.Verbose,
                TokenEstimationRatio = templateOptions.TokenEstimationRatio,
                ContextWindowSafetyMarginPercent = templateOptions.ContextWindowSafetyMarginPercent,
                AutoCompactEnabled = templateOptions.AutoCompactEnabled,
                ContextWarningThresholdPercent = templateOptions.ContextWarningThresholdPercent,
                CompactionStrategy = templateOptions.CompactionStrategy,
                CompactionPreserveTurns = templateOptions.CompactionPreserveTurns,
                CommandName = templateOptions.CommandName,
                ConfigDirectory = templateOptions.ConfigDirectory,
                EndpointSelectionSource = templateOptions.EndpointSelectionSource,
                CliOverridesApplied = new List<string>(templateOptions.CliOverridesApplied),
                McpSupported = templateOptions.McpSupported,
                McpConfigured = templateOptions.McpConfigured,
                McpServerCount = templateOptions.McpServerCount,
                BuiltInToolCount = templateOptions.BuiltInToolCount,
                EffectiveToolCount = templateOptions.EffectiveToolCount,
                AdditionalTools = templateOptions.AdditionalTools == null ? null : new List<ToolDefinition>(templateOptions.AdditionalTools),
                PromptUserFunc = templateOptions.PromptUserFunc,
                ExternalToolExecutor = templateOptions.ExternalToolExecutor,
                OnRetry = templateOptions.OnRetry,
                MuxSettings = templateOptions.MuxSettings,
                IgnoreCertErrors = templateOptions.IgnoreCertErrors
            };

            return options;
        }

        private static async IAsyncEnumerable<AgentEvent> RunAgentLoopAsync(
            AgentLoopOptions options,
            string prompt,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            using (AgentLoop loop = new AgentLoop(options))
            {
                await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, cancellationToken).ConfigureAwait(false))
                {
                    yield return agentEvent;
                }
            }
        }

        #endregion
    }
}
