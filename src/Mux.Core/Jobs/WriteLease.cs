namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A fair, single-writer workspace lease. At most one job holds the lease at a time; waiting jobs
    /// are granted it in FIFO order as it is released. Read-only tools never take the lease and so run
    /// concurrently across jobs; mutating tools acquire it and therefore serialize.
    /// The lease is <b>not reentrant</b>: a job holding the lease must release it before acquiring again.
    /// All members are thread-safe.
    /// </summary>
    public sealed class WriteLease
    {
        #region Private-Members

        private readonly object _SyncRoot = new object();
        private readonly Queue<WriteLeaseWaiter> _Waiters = new Queue<WriteLeaseWaiter>();
        private bool _Held = false;
        private string? _HolderJobId = null;
        private int _AcquisitionTimeoutMs = Timeout.Infinite;

        #endregion

        #region Public-Members

        /// <summary>
        /// The maximum time, in milliseconds, to wait for the lease before failing with a
        /// <see cref="WriteLeaseTimeoutException"/>. Defaults to <see cref="Timeout.Infinite"/> (-1),
        /// meaning wait indefinitely. Any negative value is normalized to infinite; <c>0</c> fails
        /// immediately when the lease is contended.
        /// </summary>
        public int AcquisitionTimeoutMs
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _AcquisitionTimeoutMs;
                }
            }
            set
            {
                lock (_SyncRoot)
                {
                    _AcquisitionTimeoutMs = value < 0 ? Timeout.Infinite : value;
                }
            }
        }

        /// <summary>
        /// The identifier of the job currently holding the lease, or null when the lease is free.
        /// </summary>
        public string? CurrentHolderJobId
        {
            get
            {
                lock (_SyncRoot)
                {
                    return _HolderJobId;
                }
            }
        }

        /// <summary>
        /// The identifiers of jobs currently waiting for the lease, in grant order. Abandoned
        /// (cancelled or timed-out) waiters are excluded.
        /// </summary>
        public IReadOnlyList<string> WaitingJobIds
        {
            get
            {
                lock (_SyncRoot)
                {
                    List<string> ids = new List<string>();
                    foreach (WriteLeaseWaiter waiter in _Waiters)
                    {
                        if (!waiter.Completion.Task.IsCompleted)
                        {
                            ids.Add(waiter.JobId);
                        }
                    }

                    return ids;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Acquires the lease, waiting in FIFO order when another job holds it.
        /// </summary>
        /// <param name="jobId">The requesting job identifier. Must not be null or empty.</param>
        /// <param name="cancellationToken">A token that abandons the wait.</param>
        /// <returns>A handle whose disposal releases the lease.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="jobId"/> is null or empty.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the wait is cancelled by <paramref name="cancellationToken"/>.</exception>
        /// <exception cref="WriteLeaseTimeoutException">Thrown when the lease is not acquired within <see cref="AcquisitionTimeoutMs"/>.</exception>
        public async Task<WriteLeaseHandle> AcquireAsync(string jobId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(jobId))
                throw new ArgumentException("Job id cannot be null or empty.", nameof(jobId));

            cancellationToken.ThrowIfCancellationRequested();

            WriteLeaseWaiter waiter;
            int timeoutMs;
            lock (_SyncRoot)
            {
                if (!_Held)
                {
                    _Held = true;
                    _HolderJobId = jobId;
                    return new WriteLeaseHandle(this, jobId);
                }

                waiter = new WriteLeaseWaiter(jobId);
                _Waiters.Enqueue(waiter);
                timeoutMs = _AcquisitionTimeoutMs;
            }

            using (CancellationTokenSource timeoutSource = new CancellationTokenSource())
            using (CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token))
            {
                if (timeoutMs != Timeout.Infinite)
                {
                    timeoutSource.CancelAfter(timeoutMs);
                }

                using (linkedSource.Token.Register(() => waiter.Completion.TrySetCanceled(linkedSource.Token)))
                {
                    try
                    {
                        return await waiter.Completion.Task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        throw new WriteLeaseTimeoutException(
                            $"Timed out after {timeoutMs} ms acquiring the workspace write lease for job '{jobId}'.");
                    }
                }
            }
        }

        #endregion

        #region Internal-Methods

        internal void Release(WriteLeaseHandle handle)
        {
            if (handle is null) throw new ArgumentNullException(nameof(handle));

            lock (_SyncRoot)
            {
                while (_Waiters.Count > 0)
                {
                    WriteLeaseWaiter next = _Waiters.Dequeue();
                    WriteLeaseHandle nextHandle = new WriteLeaseHandle(this, next.JobId);
                    if (next.Completion.TrySetResult(nextHandle))
                    {
                        _HolderJobId = next.JobId;
                        return;
                    }
                }

                _Held = false;
                _HolderJobId = null;
            }
        }

        #endregion
    }
}
