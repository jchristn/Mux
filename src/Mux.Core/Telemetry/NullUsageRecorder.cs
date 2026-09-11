namespace Mux.Core.Telemetry
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A no-op <see cref="IUsageRecorder"/> used when telemetry is disabled, so callers can record
    /// unconditionally without null checks. Every member is a no-op.
    /// </summary>
    public sealed class NullUsageRecorder : IUsageRecorder
    {
        #region Public-Members

        /// <summary>
        /// A shared singleton instance.
        /// </summary>
        public static NullUsageRecorder Instance { get; } = new NullUsageRecorder();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="NullUsageRecorder"/> class.
        /// </summary>
        public NullUsageRecorder()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public void Record(UsageEvent usageEvent)
        {
        }

        /// <inheritdoc />
        public Task FlushAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
