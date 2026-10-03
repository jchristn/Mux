namespace Mux.Core.Observability
{
    using System;

    /// <summary>
    /// One live contributor to an observable gauge (for example a job manager contributing its queued-job
    /// count). The owner is held weakly so an owner that is never unregistered can still be collected; the
    /// read callback receives the owner so it does not capture it strongly. Thread safety: instances are
    /// immutable after construction.
    /// </summary>
    public sealed class MuxGaugeSource
    {
        #region Private-Members

        private readonly WeakReference<object> _Owner;
        private readonly Func<object, long> _Read;
        private readonly string _GaugeName;

        #endregion

        #region Public-Members

        /// <summary>
        /// The gauge this source contributes to (one of the observable-gauge names in <see cref="MuxTelemetryNames"/>).
        /// </summary>
        public string GaugeName
        {
            get => _GaugeName;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a gauge source.
        /// </summary>
        /// <param name="gaugeName">The gauge name. Must not be null.</param>
        /// <param name="owner">The owning object, held weakly. Must not be null.</param>
        /// <param name="read">Reads the current value from the owner. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public MuxGaugeSource(string gaugeName, object owner, Func<object, long> read)
        {
            _GaugeName = gaugeName ?? throw new ArgumentNullException(nameof(gaugeName));
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            _Owner = new WeakReference<object>(owner);
            _Read = read ?? throw new ArgumentNullException(nameof(read));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether this source belongs to the given owner.
        /// </summary>
        /// <param name="owner">The candidate owner.</param>
        /// <returns>True when the weakly-held owner is alive and is <paramref name="owner"/>.</returns>
        public bool IsOwnedBy(object owner)
        {
            return _Owner.TryGetTarget(out object? target) && ReferenceEquals(target, owner);
        }

        /// <summary>
        /// Whether the owner has been garbage collected.
        /// </summary>
        /// <returns>True when the owner is gone and the source can be pruned.</returns>
        public bool IsDead()
        {
            return !_Owner.TryGetTarget(out _);
        }

        /// <summary>
        /// Reads the current value. Never throws.
        /// </summary>
        /// <returns>The owner's value, or 0 when the owner is gone or the read fails.</returns>
        public long Read()
        {
            try
            {
                if (_Owner.TryGetTarget(out object? target) && target != null)
                {
                    return _Read(target);
                }
            }
            catch (Exception)
            {
                // Best-effort: a failing read contributes nothing.
            }

            return 0;
        }

        #endregion
    }
}
