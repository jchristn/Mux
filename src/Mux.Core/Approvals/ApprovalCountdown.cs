namespace Mux.Core.Approvals
{
    using System;

    /// <summary>A visible-time countdown. Pauses while an approval prompt is obscured or inactive.</summary>
    public sealed class ApprovalCountdown
    {
        private readonly TimeProvider _Clock;
        private readonly int _Seconds;
        private long _Last;
        private bool _WasVisible;
        private TimeSpan _Elapsed;

        /// <summary>Creates a countdown. Zero disables expiration. A clock can be supplied for deterministic tests.</summary>
        public ApprovalCountdown(int seconds, TimeProvider? clock = null)
        {
            _Seconds = Math.Max(0, seconds);
            _Clock = clock ?? TimeProvider.System;
            _Last = _Clock.GetTimestamp();
        }

        /// <summary>Seconds left, rounded up. Zero when disabled or expired.</summary>
        public int RemainingSeconds => Math.Max(0, _Seconds - (int)Math.Min(int.MaxValue, Math.Floor(_Elapsed.TotalSeconds)));

        /// <summary>Updates visibility and elapsed time; returns true only when an enabled, visible countdown expires.</summary>
        public bool Update(bool visible)
        {
            long now = _Clock.GetTimestamp();
            if (_WasVisible && visible) _Elapsed += _Clock.GetElapsedTime(_Last, now);
            _Last = now;
            _WasVisible = visible;
            return _Seconds > 0 && visible && RemainingSeconds == 0;
        }
    }
}
