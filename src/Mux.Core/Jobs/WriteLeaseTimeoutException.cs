namespace Mux.Core.Jobs
{
    using System;

    /// <summary>
    /// Thrown when a job fails to acquire the workspace write lease within the configured
    /// acquisition timeout (<see cref="WriteLease.AcquisitionTimeoutMs"/>).
    /// </summary>
    public class WriteLeaseTimeoutException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WriteLeaseTimeoutException"/> class.
        /// </summary>
        /// <param name="message">A message describing the timeout. Must not be null.</param>
        public WriteLeaseTimeoutException(string message)
            : base(message ?? throw new ArgumentNullException(nameof(message)))
        {
        }
    }
}
