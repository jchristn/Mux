namespace Mux.Core.Telemetry
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Records usage events for LLM calls. Implementations are best-effort: a failed or unavailable
    /// backing store is logged and swallowed, never surfaced to the agent run. Implementations must be
    /// thread-safe and non-blocking on <see cref="Record"/>, which is called on the request hot path.
    /// </summary>
    /// <remarks>
    /// Thread safety: all members are safe to call concurrently from multiple threads.
    /// </remarks>
    public interface IUsageRecorder
    {
        /// <summary>
        /// Enqueues an event for durable recording. Non-blocking and never throws; when the event is null,
        /// telemetry is disabled, or the queue is full, the call is a no-op. When
        /// <see cref="UsageEvent.TimestampUnixMs"/> is 0, the implementation stamps the current UTC time.
        /// </summary>
        /// <param name="usageEvent">The event to record. Ignored when null.</param>
        void Record(UsageEvent usageEvent);

        /// <summary>
        /// Flushes any buffered events to the backing store. Best-effort; never throws.
        /// </summary>
        /// <param name="token">A token to cancel the flush.</param>
        /// <returns>A task that completes when buffered events have been drained.</returns>
        Task FlushAsync(CancellationToken token);
    }
}
