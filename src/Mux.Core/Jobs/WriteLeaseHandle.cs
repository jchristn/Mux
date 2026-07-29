namespace Mux.Core.Jobs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A release token for the workspace write lease. Disposing the handle releases the lease and
    /// hands it to the next waiting job (if any). Disposal is idempotent and thread-safe.
    /// </summary>
    public sealed class WriteLeaseHandle : IDisposable, IAsyncDisposable
    {
        private readonly WriteLease _Lease;
        private readonly string _JobId;
        private int _Released = 0;

        internal WriteLeaseHandle(WriteLease lease, string jobId)
        {
            _Lease = lease ?? throw new ArgumentNullException(nameof(lease));
            _JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        }

        /// <summary>
        /// The identifier of the job that holds the lease through this handle.
        /// </summary>
        public string JobId
        {
            get => _JobId;
        }

        /// <summary>
        /// Releases the lease. Safe to call multiple times; only the first call has an effect.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Released, 1) == 0)
            {
                _Lease.Release(this);
            }
        }

        /// <summary>
        /// Releases the lease asynchronously. Equivalent to <see cref="Dispose"/>.
        /// </summary>
        /// <returns>A completed value task.</returns>
        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
