namespace Mux.Core.Sessions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Watches a single session's file in the shared store and raises <see cref="Changed"/> with the session's
    /// fresh, complete snapshot whenever it is written by any process. This is the one cross-surface "reload the
    /// open conversation" primitive: a surface points it at the session it is showing and re-renders from the
    /// snapshot it receives, instead of each surface re-implementing watch-plus-poll with its own guards.
    ///
    /// <para>Because the store writes atomically (temp file then move), the snapshot loaded after a change is
    /// internally consistent — no polling is needed. The load runs on a background timer thread, so a UI consumer
    /// must marshal its re-render onto its own thread. Handlers must not throw. The subscription ignores writes to
    /// other sessions. It is inert after <see cref="Dispose"/>.</para>
    /// </summary>
    public sealed class SessionTranscriptSubscription : IDisposable
    {
        #region Private-Members

        private readonly SessionStore _Store;
        private readonly string _SessionId;
        private readonly SessionStoreWatcher _Watcher;
        private readonly object _Sync = new object();
        private bool _Disposed;

        #endregion

        #region Public-Events

        /// <summary>Raised with the session's fresh full snapshot when its file changes on disk.</summary>
        public event Action<SessionSnapshot>? Changed;

        /// <summary>Raised (with the session id) when the session's file is removed from disk.</summary>
        public event Action? Removed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a subscription over one session in a store.
        /// </summary>
        /// <param name="store">The store holding the session. Required.</param>
        /// <param name="sessionId">The session id to watch. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is null or empty.</exception>
        public SessionTranscriptSubscription(SessionStore store, string sessionId)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session id is required.", nameof(sessionId));
            _SessionId = sessionId;
            _Watcher = new SessionStoreWatcher(store.RootDirectory);
            _Watcher.Changed += OnChanged;
            _Watcher.Removed += OnRemoved;
        }

        #endregion

        #region Public-Methods

        /// <summary>Begins watching. Best-effort; a watcher that cannot attach simply raises no events.</summary>
        public void Start()
        {
            _Watcher.Start();
        }

        /// <summary>Stops watching and releases resources. Safe to call more than once.</summary>
        public void Dispose()
        {
            lock (_Sync)
            {
                if (_Disposed) return;
                _Disposed = true;
            }

            try { _Watcher.Changed -= OnChanged; } catch (Exception) { }
            try { _Watcher.Removed -= OnRemoved; } catch (Exception) { }
            try { _Watcher.Dispose(); } catch (Exception) { }
        }

        #endregion

        #region Private-Methods

        private void OnChanged(string id)
        {
            if (!string.Equals(id, _SessionId, StringComparison.Ordinal))
            {
                return;
            }

            _ = RaiseChangedAsync();
        }

        private async Task RaiseChangedAsync()
        {
            SessionSnapshot? snapshot;
            try
            {
                snapshot = await _Store.LoadAsync(_SessionId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            if (snapshot == null)
            {
                return;
            }

            lock (_Sync)
            {
                if (_Disposed) return;
            }

            try { Changed?.Invoke(snapshot); }
            catch (Exception) { /* a handler must never break the watcher */ }
        }

        private void OnRemoved(string id)
        {
            if (!string.Equals(id, _SessionId, StringComparison.Ordinal))
            {
                return;
            }

            lock (_Sync)
            {
                if (_Disposed) return;
            }

            try { Removed?.Invoke(); }
            catch (Exception) { /* best-effort */ }
        }

        #endregion
    }
}
