namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;

    /// <summary>
    /// Watches a <see cref="SessionStore"/> directory and raises an event whenever a session file is created,
    /// modified, or removed by any process. This makes the on-disk store the source of truth for cross-surface
    /// sync: every surface shares the same <c>~/.mux/sessions</c> directory, so a turn appended on one surface
    /// (which writes the session file) is observed by every other surface's watcher — regardless of which hub,
    /// process, or run produced it. The server runs one to rebroadcast store changes to its WebSocket clients
    /// (so a thin client like the dashboard or the VS Code extension learns of an in-process surface's write);
    /// the terminal and desktop apps run one to reload directly.
    ///
    /// <para>Events are debounced per session id so the atomic temp-file-plus-move a save performs (and any
    /// burst of file-system notifications) collapses into a single raise. On the debounced flush the file's
    /// existence decides the event: present raises <see cref="Changed"/>, absent raises <see cref="Removed"/>.
    /// Handlers must not throw. Thread safety: safe for concurrent use; the debounce flush runs on a timer
    /// thread, so a UI consumer must marshal the callback to its own thread.</para>
    /// </summary>
    public sealed class SessionStoreWatcher : IDisposable
    {
        #region Private-Members

        private readonly string _Directory;
        private readonly TimeSpan _DebounceInterval;
        private readonly object _Sync = new object();
        private readonly HashSet<string> _Pending = new HashSet<string>(StringComparer.Ordinal);
        private readonly Timer _Debounce;

        private FileSystemWatcher? _Watcher;
        private bool _Disposed;

        #endregion

        #region Public-Events

        /// <summary>Raised (with the session id) when a session file appears or changes on disk.</summary>
        public event Action<string>? Changed;

        /// <summary>Raised (with the session id) when a session file is removed from disk.</summary>
        public event Action<string>? Removed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a watcher over a session-store directory.
        /// </summary>
        /// <param name="directory">The directory that holds the <c>&lt;id&gt;.json</c> session files.</param>
        /// <param name="debounceMilliseconds">How long to coalesce a burst of events for one session before
        /// raising, in milliseconds. Clamped to at least 25 ms. Default 150 ms.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="directory"/> is null or empty.</exception>
        public SessionStoreWatcher(string directory, int debounceMilliseconds = 150)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required.", nameof(directory));
            _Directory = directory;
            _DebounceInterval = TimeSpan.FromMilliseconds(debounceMilliseconds < 25 ? 25 : debounceMilliseconds);
            _Debounce = new Timer(OnDebounceElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Begins watching. Creates the directory if it does not yet exist (a fresh install has not written a
        /// session), so the watcher attaches successfully and sees the first save. Best-effort: if the watcher
        /// cannot be established, the store simply produces no change events and the caller falls back to its
        /// existing refresh path.
        /// </summary>
        public void Start()
        {
            lock (_Sync)
            {
                if (_Disposed || _Watcher != null) return;

                try
                {
                    Directory.CreateDirectory(_Directory);

                    FileSystemWatcher watcher = new FileSystemWatcher(_Directory, "*.json")
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        IncludeSubdirectories = false
                    };
                    watcher.Created += OnFileEvent;
                    watcher.Changed += OnFileEvent;
                    watcher.Deleted += OnFileEvent;
                    watcher.Renamed += OnFileRenamed;
                    watcher.EnableRaisingEvents = true;
                    _Watcher = watcher;
                }
                catch (Exception)
                {
                    _Watcher = null;
                }
            }
        }

        /// <summary>Stops watching and releases resources. Safe to call more than once.</summary>
        public void Dispose()
        {
            lock (_Sync)
            {
                if (_Disposed) return;
                _Disposed = true;

                try { _Debounce.Dispose(); } catch (Exception) { }
                if (_Watcher != null)
                {
                    try { _Watcher.EnableRaisingEvents = false; } catch (Exception) { }
                    try { _Watcher.Dispose(); } catch (Exception) { }
                    _Watcher = null;
                }
            }
        }

        #endregion

        #region Private-Methods

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            Enqueue(e.FullPath);
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            // The atomic save renames "<id>.json.tmp" to "<id>.json"; the destination is what matters. Also
            // enqueue the old path in case a rename moved a real session file away.
            Enqueue(e.FullPath);
            Enqueue(e.OldFullPath);
        }

        private void Enqueue(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || !fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string id = Path.GetFileNameWithoutExtension(fullPath);
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            lock (_Sync)
            {
                if (_Disposed) return;
                _Pending.Add(id);
                try { _Debounce.Change(_DebounceInterval, Timeout.InfiniteTimeSpan); } catch (ObjectDisposedException) { }
            }
        }

        private void OnDebounceElapsed(object? state)
        {
            List<string> ids;
            lock (_Sync)
            {
                if (_Disposed || _Pending.Count == 0) return;
                ids = new List<string>(_Pending);
                _Pending.Clear();
            }

            foreach (string id in ids)
            {
                bool exists;
                try { exists = File.Exists(Path.Combine(_Directory, id + ".json")); }
                catch (Exception) { exists = true; }

                try
                {
                    if (exists) { Changed?.Invoke(id); }
                    else { Removed?.Invoke(id); }
                }
                catch (Exception)
                {
                    // A handler must never break the watcher.
                }
            }
        }

        #endregion
    }
}
