namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Jobs;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="WriteLease"/>: single-writer serialization, FIFO fairness,
    /// timeout, cancellation, and handle release semantics. Read-only tools bypass the lease entirely
    /// (they never call <see cref="WriteLease.AcquireAsync"/>), so their concurrency is verified at the
    /// tool-execution integration level rather than here.
    /// </summary>
    public static class WriteLeaseSuite
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Builds the write-lease suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the write-lease cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "WriteLease",
                "Workspace write-lease serialization and fairness",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("WriteLease", "AcquireOnFreeLeaseIsImmediate", "Acquiring a free lease completes immediately and sets the holder", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease();
                        MuxAssert.IsNull(lease.CurrentHolderJobId, "initially free");
                        WriteLeaseHandle handle = await lease.AcquireAsync("j1", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j1", lease.CurrentHolderJobId, "holder");
                        MuxAssert.AreEqual("j1", handle.JobId, "handle job id");
                        handle.Dispose();
                        MuxAssert.IsNull(lease.CurrentHolderJobId, "free after release");
                    }),

                    new TestCaseDescriptor("WriteLease", "SecondAcquireWaitsUntilRelease", "A second acquire waits until the holder releases, then is granted", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease();
                        WriteLeaseHandle first = await lease.AcquireAsync("j1", ct).ConfigureAwait(false);

                        Task<WriteLeaseHandle> secondTask = lease.AcquireAsync("j2", ct);
                        MuxAssert.IsFalse(secondTask.IsCompleted, "second waits while held");
                        MuxAssert.AreEqual(1, lease.WaitingJobIds.Count, "one waiter");
                        MuxAssert.AreEqual("j2", lease.WaitingJobIds[0], "waiter is j2");

                        first.Dispose();
                        WriteLeaseHandle second = await secondTask.WaitAsync(Guard, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j2", lease.CurrentHolderJobId, "holder j2");
                        MuxAssert.AreEqual(0, lease.WaitingJobIds.Count, "no waiters");
                        second.Dispose();
                        MuxAssert.IsNull(lease.CurrentHolderJobId, "free after release");
                    }),

                    new TestCaseDescriptor("WriteLease", "WaitersGrantedInFifoOrder", "Queued waiters are granted the lease in FIFO order", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease();
                        WriteLeaseHandle first = await lease.AcquireAsync("j1", ct).ConfigureAwait(false);

                        Task<WriteLeaseHandle> second = lease.AcquireAsync("j2", ct);
                        Task<WriteLeaseHandle> third = lease.AcquireAsync("j3", ct);

                        IReadOnlyList<string> waiting = lease.WaitingJobIds;
                        MuxAssert.AreEqual(2, waiting.Count, "two waiters");
                        MuxAssert.AreEqual("j2", waiting[0], "first waiter");
                        MuxAssert.AreEqual("j3", waiting[1], "second waiter");

                        first.Dispose();
                        WriteLeaseHandle secondHandle = await second.WaitAsync(Guard, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j2", lease.CurrentHolderJobId, "j2 granted first");
                        MuxAssert.IsFalse(third.IsCompleted, "j3 still waiting");

                        secondHandle.Dispose();
                        WriteLeaseHandle thirdHandle = await third.WaitAsync(Guard, ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j3", lease.CurrentHolderJobId, "j3 granted second");
                        thirdHandle.Dispose();
                    }),

                    new TestCaseDescriptor("WriteLease", "AcquireTimesOutWhenContended", "Acquire fails with WriteLeaseTimeoutException when the lease is not freed in time", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease { AcquisitionTimeoutMs = 50 };
                        WriteLeaseHandle first = await lease.AcquireAsync("j1", ct).ConfigureAwait(false);

                        await MuxAssert.ThrowsAsync<WriteLeaseTimeoutException>(
                            async () => await lease.AcquireAsync("j2", ct).ConfigureAwait(false),
                            "timeout while contended").ConfigureAwait(false);

                        first.Dispose();
                        WriteLeaseHandle after = await lease.AcquireAsync("j3", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j3", lease.CurrentHolderJobId, "acquirable after release");
                        after.Dispose();
                    }),

                    new TestCaseDescriptor("WriteLease", "CancellationWhileWaitingThrowsAndLeaseRecovers", "Cancelling a waiter throws OperationCanceledException and does not wedge the lease", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease();
                        WriteLeaseHandle first = await lease.AcquireAsync("j1", ct).ConfigureAwait(false);

                        using (CancellationTokenSource waiterCts = new CancellationTokenSource())
                        {
                            Task<WriteLeaseHandle> waiting = lease.AcquireAsync("j2", waiterCts.Token);
                            MuxAssert.AreEqual(1, lease.WaitingJobIds.Count, "one waiter");
                            waiterCts.Cancel();
                            await MuxAssert.ThrowsAsync<OperationCanceledException>(
                                async () => await waiting.WaitAsync(Guard, ct).ConfigureAwait(false),
                                "cancelled waiter").ConfigureAwait(false);
                        }

                        first.Dispose();
                        WriteLeaseHandle third = await lease.AcquireAsync("j3", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j3", lease.CurrentHolderJobId, "lease recovers after cancelled waiter");
                        third.Dispose();
                    }),

                    new TestCaseDescriptor("WriteLease", "HandleDisposeReleasesEvenOnException", "A using-scoped handle releases the lease even when the body throws", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease();
                        try
                        {
                            using (WriteLeaseHandle handle = await lease.AcquireAsync("j1", ct).ConfigureAwait(false))
                            {
                                throw new InvalidOperationException("boom");
                            }
                        }
                        catch (InvalidOperationException)
                        {
                        }

                        MuxAssert.IsNull(lease.CurrentHolderJobId, "released despite exception");
                        WriteLeaseHandle next = await lease.AcquireAsync("j2", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j2", lease.CurrentHolderJobId, "reacquirable");
                        next.Dispose();
                    }),

                    new TestCaseDescriptor("WriteLease", "DoubleDisposeIsIdempotent", "Disposing a handle twice releases only once and does not affect a later holder", async (CancellationToken ct) =>
                    {
                        WriteLease lease = new WriteLease();
                        WriteLeaseHandle first = await lease.AcquireAsync("j1", ct).ConfigureAwait(false);
                        first.Dispose();
                        first.Dispose();

                        WriteLeaseHandle second = await lease.AcquireAsync("j2", ct).ConfigureAwait(false);
                        MuxAssert.AreEqual("j2", lease.CurrentHolderJobId, "holder j2");
                        first.Dispose();
                        MuxAssert.AreEqual("j2", lease.CurrentHolderJobId, "stale dispose does not release j2");
                        second.Dispose();
                        MuxAssert.IsNull(lease.CurrentHolderJobId, "free");
                    })
                });
        }
    }
}
