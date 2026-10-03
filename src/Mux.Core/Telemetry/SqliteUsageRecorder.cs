namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Mux.Core.Observability;

    /// <summary>
    /// A best-effort <see cref="IUsageRecorder"/> that buffers events in an in-memory channel and drains
    /// them to a <see cref="SqliteUsageStore"/> on a single background task. <see cref="Record"/> is
    /// non-blocking and never throws: when the buffer is full the event is dropped rather than blocking
    /// the request hot path. Because the backing store is WAL-mode SQLite, this single-writer-per-process
    /// design also means each process holds the database write lock only in short bursts, which keeps
    /// concurrent mux instances out of each other's way.
    /// </summary>
    /// <remarks>
    /// Thread safety: all members are safe to call concurrently. Dispose (or <see cref="DisposeAsync"/>)
    /// once at process shutdown to flush the buffer.
    /// </remarks>
    public sealed class SqliteUsageRecorder : IUsageRecorder, IAsyncDisposable, IDisposable
    {
        #region Private-Members

        private const int BatchSize = 256;

        private readonly SqliteUsageStore _Store;
        private readonly Action<string>? _Logger;
        private readonly Channel<UsageEvent> _Channel;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly Task _WriterLoop;
        private readonly Task _MaintenanceLoop;
        private long _DroppedCount = 0;
        private long _PendingCount = 0;
        private int _LastPruneDayNumber = -1;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The number of events dropped because the buffer was full. Useful for diagnostics; a non-zero
        /// value means telemetry is being produced faster than it can be written.
        /// </summary>
        public long DroppedCount
        {
            get => Interlocked.Read(ref _DroppedCount);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SqliteUsageRecorder"/> class and starts its
        /// background writer.
        /// </summary>
        /// <param name="store">The backing store. Required.</param>
        /// <param name="logger">An optional sink for best-effort error messages. Null discards them.</param>
        /// <param name="capacity">The buffer capacity before events are dropped. Floored at 256.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public SqliteUsageRecorder(SqliteUsageStore store, Action<string>? logger = null, int capacity = 10000)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _Logger = logger;

            BoundedChannelOptions options = new BoundedChannelOptions(Math.Max(256, capacity))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            };
            _Channel = Channel.CreateBounded<UsageEvent>(options);
            MuxTelemetry.RegisterGaugeSource(MuxTelemetryNames.UsageQueueDepth, this, (object owner) => Interlocked.Read(ref ((SqliteUsageRecorder)owner)._PendingCount));
            _WriterLoop = Task.Run(() => WriterLoopAsync(_Cts.Token));
            _MaintenanceLoop = Task.Run(() => MaintenanceLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Record(UsageEvent usageEvent)
        {
            if (usageEvent == null || _Disposed)
            {
                return;
            }

            // Every telemetry entry must be tied to a conversation. Drop (and count) any event without a
            // session id rather than persist an orphan that cannot be attributed to a conversation.
            if (string.IsNullOrWhiteSpace(usageEvent.SessionId))
            {
                Interlocked.Increment(ref _DroppedCount);
                MuxTelemetry.RecordUsageEvents("dropped", 1);
                Log("usage event dropped: no session id");
                return;
            }

            if (usageEvent.TimestampUnixMs == 0)
            {
                usageEvent.TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            if (_Channel.Writer.TryWrite(usageEvent))
            {
                Interlocked.Increment(ref _PendingCount);
                MuxTelemetry.RecordUsageEvents("enqueued", 1);
            }
            else
            {
                Interlocked.Increment(ref _DroppedCount);
                MuxTelemetry.RecordUsageEvents("dropped", 1);
            }
        }

        /// <inheritdoc />
        public async Task FlushAsync(CancellationToken token)
        {
            // Best-effort: wait until every enqueued event has been durably written (not merely dequeued),
            // bounded so a wedged writer cannot hang a caller. Draining the channel's queue is not enough:
            // the writer removes a batch from the channel (dropping its Count to zero) before the async
            // SQLite insert completes, so a Count-only wait can return before the rows are visible to a new
            // query connection. _PendingCount is only decremented once the batch has been persisted.
            for (int i = 0; i < 200; i++)
            {
                if (Interlocked.Read(ref _PendingCount) <= 0)
                {
                    return;
                }

                token.ThrowIfCancellationRequested();
                await Task.Delay(25, token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Flushes buffered events and stops the background writer.
        /// </summary>
        /// <returns>A task that completes when the writer has drained and stopped.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed)
            {
                return;
            }

            _Disposed = true;
            MuxTelemetry.UnregisterGaugeSources(this);
            _Cts.Cancel();
            _Channel.Writer.TryComplete();

            try
            {
                await _WriterLoop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log("usage recorder shutdown error: " + ex.Message);
            }

            try
            {
                await _MaintenanceLoop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log("usage maintenance shutdown error: " + ex.Message);
            }

            _Cts.Dispose();
        }

        /// <summary>
        /// Flushes buffered events and stops the background writer synchronously.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed)
            {
                return;
            }

            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        #endregion

        #region Private-Methods

        private async Task WriterLoopAsync(CancellationToken token)
        {
            // The loop was started from whatever context constructed the recorder; detach so each batch span
            // is a root rather than a child of an unrelated (long-finished) request.
            Activity.Current = null;
            List<UsageEvent> batch = new List<UsageEvent>(BatchSize);

            try
            {
                while (await _Channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    batch.Clear();

                    while (batch.Count < BatchSize && _Channel.Reader.TryRead(out UsageEvent? item))
                    {
                        if (item != null)
                        {
                            batch.Add(item);
                        }
                    }

                    if (batch.Count > 0)
                    {
                        await WriteBatchAsync(batch, token).ConfigureAwait(false);
                    }

                    await MaybePruneAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested; fall through to a final drain below.
            }
            catch (Exception ex)
            {
                Log("usage recorder writer loop error: " + ex.Message);
            }

            // Final drain on shutdown so buffered events are not lost.
            batch.Clear();
            while (_Channel.Reader.TryRead(out UsageEvent? item))
            {
                if (item != null)
                {
                    batch.Add(item);
                }
            }

            if (batch.Count > 0)
            {
                await WriteBatchAsync(batch, CancellationToken.None).ConfigureAwait(false);
            }
        }

        private async Task WriteBatchAsync(IReadOnlyList<UsageEvent> batch, CancellationToken token)
        {
            // Background work: each batch is its own root span (there is no inbound request to parent it to).
            Activity? activity = MuxTelemetry.StartActivity("usage write_batch", ActivityKind.Internal, default(ActivityContext));
            MuxTelemetry.SetTag(activity, MuxTelemetryNames.AttrBatchSize, batch.Count);
            long startTimestamp = Stopwatch.GetTimestamp();
            try
            {
                await _Store.InsertBatchAsync(batch, token).ConfigureAwait(false);
                MuxTelemetry.SetOk(activity);
                MuxTelemetry.RecordUsageEvents("written", batch.Count);
                MuxTelemetry.RecordUsageWrite(MuxTelemetryNames.OutcomeSuccess, MuxTelemetry.SecondsSince(startTimestamp));
            }
            catch (OperationCanceledException)
            {
                MuxTelemetry.SetError(activity, MuxTelemetryNames.OutcomeCancelled, "usage write cancelled");
                MuxTelemetry.RecordUsageWrite(MuxTelemetryNames.OutcomeCancelled, MuxTelemetry.SecondsSince(startTimestamp));
                throw;
            }
            catch (Exception ex)
            {
                MuxTelemetry.RecordException(activity, ex);
                MuxTelemetry.RecordUsageEvents("write_failed", batch.Count);
                MuxTelemetry.RecordUsageWrite(MuxTelemetryNames.OutcomeError, MuxTelemetry.SecondsSince(startTimestamp));
                Log("usage recorder insert failed (" + batch.Count + " events dropped): " + ex.Message);
            }
            finally
            {
                MuxTelemetry.Stop(activity);
                // These events are now durably written (or dropped after a failure): either way they are no
                // longer pending, so FlushAsync callers waiting on _PendingCount can proceed.
                Interlocked.Add(ref _PendingCount, -batch.Count);
            }
        }

        private async Task MaybePruneAsync(CancellationToken token)
        {
            int today = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / (24L * 60L * 60L * 1000L));
            if (today == _LastPruneDayNumber)
            {
                return;
            }

            _LastPruneDayNumber = today;

            try
            {
                await _Store.PruneAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("usage recorder prune failed: " + ex.Message);
            }
        }

        // Runs independently of write activity so retention is enforced even while the app sits idle: purges
        // any orphaned (no-conversation) rows once at startup, then prunes past the retention window hourly.
        private async Task MaintenanceLoopAsync(CancellationToken token)
        {
            try
            {
                try
                {
                    await _Store.DeleteOrphansAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log("usage orphan purge failed: " + ex.Message);
                }

                while (!token.IsCancellationRequested)
                {
                    await MaybePruneAsync(token).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromHours(1), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested.
            }
            catch (Exception ex)
            {
                Log("usage maintenance loop error: " + ex.Message);
            }
        }

        private void Log(string message)
        {
            _Logger?.Invoke(message);
        }

        #endregion
    }
}
