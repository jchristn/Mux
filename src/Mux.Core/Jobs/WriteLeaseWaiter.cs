namespace Mux.Core.Jobs
{
    using System.Threading.Tasks;

    /// <summary>
    /// Internal record of a job waiting for the workspace write lease. Each waiter carries the
    /// requesting job id and a completion source that is resolved with a handle when the lease is
    /// granted, or cancelled when the wait is abandoned (cancellation or timeout).
    /// </summary>
    internal sealed class WriteLeaseWaiter
    {
        private readonly string _JobId;
        private readonly TaskCompletionSource<WriteLeaseHandle> _Completion =
            new TaskCompletionSource<WriteLeaseHandle>(TaskCreationOptions.RunContinuationsAsynchronously);

        internal WriteLeaseWaiter(string jobId)
        {
            _JobId = jobId;
        }

        internal string JobId
        {
            get => _JobId;
        }

        internal TaskCompletionSource<WriteLeaseHandle> Completion
        {
            get => _Completion;
        }
    }
}
