namespace Mux.Cli.Commands
{
    using System;

    /// <summary>
    /// Represents a queued interactive prompt awaiting execution.
    /// </summary>
    public class QueuedMessageEntry
    {
        #region Public-Members

        /// <summary>
        /// Stable identifier for the queued entry.
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Monotonic sequence number assigned when the prompt is queued.
        /// </summary>
        public int SequenceNumber { get; set; }

        /// <summary>
        /// UTC timestamp indicating when the prompt was queued.
        /// </summary>
        public DateTime EnqueuedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Prompt text to execute later.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        #endregion
    }
}
