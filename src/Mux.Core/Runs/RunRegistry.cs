namespace Mux.Core.Runs
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Server-lifetime registry of active and recently-terminal <see cref="RunHandle"/> instances, keyed by
    /// run id. Gives the cancel-run route, the run-state inspection routes, and the WebSocket bridge a shared
    /// handle to name, observe, and stop a run independently of the HTTP request that started it. Terminal
    /// runs are retained for <see cref="RetentionWindow"/> so a state poll shortly after completion still
    /// resolves, then evicted by a background loop.
    ///
    /// <para>Thread safety: safe for concurrent use.</para>
    /// </summary>
    public sealed class RunRegistry : IDisposable
    {
        #region Private-Members

        private readonly ConcurrentDictionary<string, RunHandle> _Runs = new ConcurrentDictionary<string, RunHandle>();
        private readonly List<Action<string>> _SessionsListeners = new List<Action<string>>();
        private readonly object _ListenerSync = new object();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly Task _EvictionLoop;
        private readonly TimeSpan _PollInterval = TimeSpan.FromMilliseconds(500);
        private TimeSpan _RetentionWindow = TimeSpan.FromMinutes(5);
        private bool _Disposed;

        #endregion

        #region Public-Events

        /// <summary>
        /// Raised when a new run is registered (via <see cref="Create"/> or the first
        /// <see cref="GetOrCreate"/> for a run id). A session-scoped subscriber uses this to attach to runs
        /// that start after it subscribed — the mechanism behind "mirror on by default": a client subscribed
        /// to a session sees the next run for it the moment it begins. Handlers must not throw.
        /// </summary>
        public event Action<RunHandle>? RunRegistered;

        #endregion

        #region Sessions-Notifications

        /// <summary>
        /// Registers a listener notified (with the affected session id, possibly empty) whenever the
        /// conversation list may have changed — a run completed, or a surface signalled a rename/delete via
        /// <see cref="NotifySessionsChanged"/>. Surfaces use this to refresh their conversation list live
        /// instead of relying on a manual refresh.
        /// </summary>
        /// <param name="listener">The callback. Ignored when null.</param>
        public void AddSessionsListener(Action<string> listener)
        {
            if (listener == null) return;
            lock (_ListenerSync) { _SessionsListeners.Add(listener); }
        }

        /// <summary>Removes a previously-added sessions listener.</summary>
        /// <param name="listener">The callback to remove.</param>
        public void RemoveSessionsListener(Action<string> listener)
        {
            if (listener == null) return;
            lock (_ListenerSync) { _SessionsListeners.Remove(listener); }
        }

        /// <summary>
        /// Notifies all sessions listeners that the conversation list may have changed. Called on run
        /// completion and when a surface signals an out-of-band change (rename/duplicate/delete).
        /// </summary>
        /// <param name="sessionId">The affected session id, or empty for a non-specific change.</param>
        public void NotifySessionsChanged(string sessionId)
        {
            List<Action<string>> listeners;
            lock (_ListenerSync) { listeners = new List<Action<string>>(_SessionsListeners); }
            foreach (Action<string> listener in listeners)
            {
                try { listener(sessionId ?? string.Empty); }
                catch (Exception) { /* a listener must never break the notifier */ }
            }
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// How long a terminal run is retained for inspection before eviction. Minimum 1 second.
        /// Default 5 minutes.
        /// </summary>
        public TimeSpan RetentionWindow
        {
            get => _RetentionWindow;
            set => _RetentionWindow = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the registry and start its background eviction loop.
        /// </summary>
        public RunRegistry()
        {
            _EvictionLoop = Task.Run(() => EvictionLoopAsync(_Cts.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates and registers a new run handle.
        /// </summary>
        /// <param name="runId">Run correlation id. Must not be null or empty.</param>
        /// <param name="sessionId">Persisted session id, or empty.</param>
        /// <param name="endpointName">Endpoint the run targets.</param>
        /// <param name="model">Model the run targets.</param>
        /// <param name="externalToken">Token linked into the run's cancellation source (typically the request token).</param>
        /// <returns>The registered <see cref="RunHandle"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="runId"/> is null or empty.</exception>
        public RunHandle Create(string runId, string sessionId, string endpointName, string model, CancellationToken externalToken)
        {
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("Run id is required.", nameof(runId));

            RunHandle handle = new RunHandle(runId, sessionId, endpointName, model, externalToken);
            handle.Completed += () => NotifySessionsChanged(handle.SessionId);
            _Runs[runId] = handle;
            RaiseRegistered(handle);
            return handle;
        }

        /// <summary>
        /// Returns the existing handle for a run id, or creates and registers one when it is not yet tracked.
        /// Used by the WebSocket bridge when a producer publishes events for a run that started in another
        /// process — the first published frame materializes the handle in this hub's registry so subscribers
        /// can observe it. The created handle has no linked external token (a published run is not cancelable
        /// from the hub).
        /// </summary>
        /// <param name="runId">Run correlation id. Must not be null or empty.</param>
        /// <param name="sessionId">Persisted session id, or empty.</param>
        /// <param name="endpointName">Endpoint the run targets.</param>
        /// <param name="model">Model the run targets.</param>
        /// <returns>The existing or newly-registered <see cref="RunHandle"/>.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="runId"/> is null or empty.</exception>
        public RunHandle GetOrCreate(string runId, string sessionId, string endpointName, string model)
        {
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("Run id is required.", nameof(runId));

            bool created = false;
            RunHandle handle = _Runs.GetOrAdd(runId, id =>
            {
                created = true;
                return new RunHandle(id, sessionId ?? string.Empty, endpointName ?? string.Empty, model ?? string.Empty, CancellationToken.None);
            });

            if (created)
            {
                handle.Completed += () => NotifySessionsChanged(handle.SessionId);
                RaiseRegistered(handle);
            }

            return handle;
        }

        /// <summary>
        /// Returns the currently-tracked handles for a session (active first, most recent first). Used by a
        /// session-scoped subscriber to attach to any run already in flight when it subscribes.
        /// </summary>
        /// <param name="sessionId">The session id. Null or empty yields an empty list.</param>
        /// <returns>The matching handles.</returns>
        public IReadOnlyList<RunHandle> FindBySession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return new List<RunHandle>();
            }

            return _Runs.Values
                .Where(h => string.Equals(h.SessionId, sessionId, StringComparison.Ordinal))
                .OrderBy(h => h.IsTerminal)
                .ThenByDescending(h => h.StartedUtc)
                .ToList();
        }

        /// <summary>
        /// Looks up a run handle by id.
        /// </summary>
        /// <param name="runId">The run id.</param>
        /// <param name="handle">The resolved handle when found.</param>
        /// <returns>True when a handle was found; otherwise false.</returns>
        public bool TryGet(string runId, out RunHandle? handle)
        {
            if (string.IsNullOrEmpty(runId))
            {
                handle = null;
                return false;
            }

            return _Runs.TryGetValue(runId, out handle);
        }

        /// <summary>
        /// Requests cancellation of an active run by id.
        /// </summary>
        /// <param name="runId">The run id.</param>
        /// <returns>True when an active run was found and cancellation was requested; false when the run is
        /// unknown, already terminal, or already canceled.</returns>
        public bool TryCancel(string runId)
        {
            if (string.IsNullOrEmpty(runId)) return false;

            if (_Runs.TryGetValue(runId, out RunHandle? handle) && handle != null)
            {
                return handle.Cancel();
            }

            return false;
        }

        /// <summary>
        /// Snapshots the currently-tracked run handles (active and recently-terminal), most recent first.
        /// </summary>
        /// <returns>A read-only list of handles.</returns>
        public IReadOnlyList<RunHandle> List()
        {
            return _Runs.Values.OrderByDescending(h => h.StartedUtc).ToList();
        }

        /// <summary>
        /// Marks a run terminal with the given status when the run produced no terminal event (for example a
        /// cancellation). No-op when the run is unknown.
        /// </summary>
        /// <param name="runId">The run id.</param>
        /// <param name="status">The terminal status to record.</param>
        public void Complete(string runId, RunStatusEnum status)
        {
            if (string.IsNullOrEmpty(runId)) return;

            if (_Runs.TryGetValue(runId, out RunHandle? handle) && handle != null)
            {
                handle.MarkTerminal(status);
            }
        }

        /// <summary>
        /// Stops the eviction loop and disposes all tracked handles.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            try { _Cts.Cancel(); } catch (ObjectDisposedException) { }
            try { _EvictionLoop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }

            foreach (RunHandle handle in _Runs.Values)
            {
                try { handle.Dispose(); } catch (Exception) { }
            }

            _Runs.Clear();
            try { _Cts.Dispose(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #region Private-Methods

        private void RaiseRegistered(RunHandle handle)
        {
            Action<RunHandle>? handler = RunRegistered;
            if (handler == null)
            {
                return;
            }

            try
            {
                handler(handle);
            }
            catch (Exception)
            {
                // A subscriber's handler must never disrupt run registration.
            }
        }

        private async Task EvictionLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_PollInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                DateTime cutoff = DateTime.UtcNow - _RetentionWindow;
                foreach (KeyValuePair<string, RunHandle> pair in _Runs)
                {
                    DateTime? completedUtc = pair.Value.CompletedUtc;
                    if (completedUtc.HasValue && completedUtc.Value < cutoff)
                    {
                        if (_Runs.TryRemove(pair.Key, out RunHandle? removed) && removed != null)
                        {
                            try { removed.Dispose(); } catch (Exception) { }
                        }
                    }
                }
            }
        }

        #endregion
    }
}
