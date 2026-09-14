namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;

    /// <summary>
    /// Default <see cref="IThreadService"/> for the desktop app. A thread is one persisted session; this
    /// service is a thin adapter over the shared <see cref="ISessionManager"/> (the single implementation of
    /// session-management verbs used by every surface), mapping <see cref="SessionInfo"/> to the desktop's
    /// <see cref="ThreadSummary"/> projection. Thread ids are opaque GUIDs so they are always valid file names.
    /// </summary>
    public sealed class ThreadService : IThreadService
    {
        private readonly ISessionManager _Sessions;

        /// <summary>
        /// Instantiate the thread service over a session store.
        /// </summary>
        /// <param name="store">The session store backing the threads. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public ThreadService(SessionStore store)
            : this(new SessionManager(store ?? throw new ArgumentNullException(nameof(store))))
        {
        }

        /// <summary>
        /// Instantiate the thread service over an explicit session manager.
        /// </summary>
        /// <param name="sessions">The shared session manager. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sessions"/> is null.</exception>
        public ThreadService(ISessionManager sessions)
        {
            _Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ThreadSummary>> ListAsync(CancellationToken token)
        {
            IReadOnlyList<SessionInfo> sessions = await _Sessions.ListAsync(token).ConfigureAwait(false);
            List<ThreadSummary> summaries = new List<ThreadSummary>(sessions.Count);
            foreach (SessionInfo info in sessions)
            {
                summaries.Add(MapToSummary(info));
            }

            return summaries;
        }

        /// <inheritdoc />
        public async Task<ThreadSummary> CreateAsync(string? title, string? endpointName, string? model, CancellationToken token)
        {
            SessionInfo info = await _Sessions.CreateAsync(title, endpointName, model, null, token).ConfigureAwait(false);
            return MapToSummary(info);
        }

        /// <inheritdoc />
        public async Task<ThreadSummary?> RenameAsync(string id, string title, CancellationToken token)
        {
            SessionInfo? info = await _Sessions.RenameAsync(id, title, token).ConfigureAwait(false);
            return info == null ? null : MapToSummary(info);
        }

        /// <inheritdoc />
        public Task<bool> SetTitlePinnedAsync(string id, bool pinned, CancellationToken token)
        {
            return _Sessions.SetTitlePinnedAsync(id, pinned, token);
        }

        /// <inheritdoc />
        public async Task<ThreadSummary?> DuplicateAsync(string id, CancellationToken token)
        {
            SessionInfo? info = await _Sessions.DuplicateAsync(id, token).ConfigureAwait(false);
            return info == null ? null : MapToSummary(info);
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token)
        {
            return _Sessions.DeleteAsync(id, token);
        }

        /// <inheritdoc />
        public Task<string?> ExportAsync(string id, string format, CancellationToken token)
        {
            return _Sessions.ExportAsync(id, format, token);
        }

        private static ThreadSummary MapToSummary(SessionInfo info)
        {
            return new ThreadSummary(
                info.Id,
                info.Title,
                info.EndpointName,
                info.Model,
                info.CreatedUtc,
                info.UpdatedUtc,
                info.MessageCount,
                info.TitlePinned);
        }
    }
}
