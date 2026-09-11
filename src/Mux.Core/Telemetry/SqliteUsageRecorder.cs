namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;

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
            _WriterLoop = Task.Run(() => WriterLoopAsync(_Cts.Token));
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

            if (usageEvent.TimestampUnixMs == 0)
            {
                usageEvent.TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            if (_Channel.Writer.TryWrite(usageEvent))
            {
                Interlocked.Increment(ref _PendingCount);
            }
            else
            {
                Interlocked.Increment(ref _DroppedCount);
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
            _Channel.Writer.TryComplete();

            try
            {
                await _WriterLoop.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log("usage recorder shutdown error: " + ex.Message);
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
            try
            {
                await _Store.InsertBatchAsync(batch, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("usage recorder insert failed (" + batch.Count + " events dropped): " + ex.Message);
            }
            finally
            {
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

        private void Log(string message)
        {
            _Logger?.Invoke(message);
        }

        #endregion
    }
}
