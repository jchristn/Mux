namespace Test.Shared.Support
{
    using System;

    /// <summary>
    /// A <see cref="TimeProvider"/> whose time moves only when a test advances it, for deterministic scheduler tests.
    /// </summary>
    public sealed class ManualClock : TimeProvider
    {
        #region Private-Members

        private readonly object _Sync = new object();
        private DateTimeOffset _Now;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualClock"/> class at a fixed start time.
        /// </summary>
        /// <param name="start">The starting time (UTC).</param>
        public ManualClock(DateTimeOffset start)
        {
            _Now = start;
        }

        #endregion

        #region Public-Members

        /// <summary>The current time as a UTC <see cref="DateTime"/>.</summary>
        public DateTime UtcNow
        {
            get { lock (_Sync) { return _Now.UtcDateTime; } }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override DateTimeOffset GetUtcNow()
        {
            lock (_Sync)
            {
                return _Now;
            }
        }

        /// <summary>
        /// Moves the clock forward.
        /// </summary>
        /// <param name="amount">How far to move it. Negative values are ignored.</param>
        public void Advance(TimeSpan amount)
        {
            if (amount <= TimeSpan.Zero)
            {
                return;
            }

            lock (_Sync)
            {
                _Now = _Now.Add(amount);
            }
        }

        #endregion
    }
}
