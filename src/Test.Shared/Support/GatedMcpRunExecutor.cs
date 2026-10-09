namespace Test.Shared.Support
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.McpServer;

    /// <summary>
    /// An <see cref="IMcpRunExecutor"/> for concurrency tests: each run signals that it started, then waits until the
    /// test releases it (or the run is cancelled). Records the highest number of runs active at once, and can be told
    /// to throw instead of answering.
    /// </summary>
    public sealed class GatedMcpRunExecutor : IMcpRunExecutor
    {
        #region Private-Members

        private readonly SemaphoreSlim _Release = new SemaphoreSlim(0);
        private readonly SemaphoreSlim _Started = new SemaphoreSlim(0);
        private int _Active;
        private int _MaxActive;
        private int _Completed;

        #endregion

        #region Public-Members

        /// <summary>The most runs that were active at the same time.</summary>
        public int MaxActive => Volatile.Read(ref _MaxActive);

        /// <summary>How many runs finished (answered, threw, or were cancelled).</summary>
        public int Completed => Volatile.Read(ref _Completed);

        /// <summary>When set, a released run throws this message instead of answering.</summary>
        public string? ThrowMessage { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>Lets one waiting run finish.</summary>
        public void ReleaseOne()
        {
            _Release.Release();
        }

        /// <summary>Waits until a run has started.</summary>
        /// <param name="timeout">How long to wait.</param>
        /// <returns>True when a run started in time.</returns>
        public Task<bool> WaitStartedAsync(TimeSpan timeout)
        {
            return _Started.WaitAsync(timeout);
        }

        /// <inheritdoc/>
        public async Task<McpRunResult> RunAsync(McpRunRequest request, Func<string, Task> progress, CancellationToken cancellationToken)
        {
            int now = Interlocked.Increment(ref _Active);
            int seen;
            while (now > (seen = Volatile.Read(ref _MaxActive)) && Interlocked.CompareExchange(ref _MaxActive, now, seen) != seen)
            {
            }

            _Started.Release();
            try
            {
                await _Release.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (ThrowMessage != null) throw new InvalidOperationException(ThrowMessage);
                return new McpRunResult { Answer = "done: " + request.Prompt, Status = "completed", Endpoint = "e", Model = "m" };
            }
            finally
            {
                Interlocked.Decrement(ref _Active);
                Interlocked.Increment(ref _Completed);
            }
        }

        #endregion
    }
}
