namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Default <see cref="ISessionManager"/> over a <see cref="SessionStore"/>. This is the one shared
    /// implementation of session-management verbs used by every surface; creation, rename, pin, duplicate,
    /// delete, and export map onto the store and the shared session helpers. Ids are opaque GUIDs so they
    /// are always valid file names.
    /// </summary>
    public sealed class SessionManager : ISessionManager
    {
        private readonly SessionStore _Store;

        /// <summary>
        /// Instantiate the session manager.
        /// </summary>
        /// <param name="store">The session store backing the sessions. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public SessionManager(SessionStore store)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<SessionInfo>> ListAsync(CancellationToken token)
        {
            IReadOnlyList<SessionSnapshot> snapshots = await _Store.ListAsync(token).ConfigureAwait(false);
            return snapshots
                .Select(SessionInfo.FromSnapshot)
                .OrderByDescending(info => info.UpdatedUtc)
                .ToList();
        }

        /// <inheritdoc />
        public async Task<SessionInfo> CreateAsync(string? title, string? endpointName, string? model, string? workingDirectory, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            SessionSnapshot snapshot = new SessionSnapshot
            {
                Id = NewId(),
                Title = SessionTitleHelper.Normalize(title, SessionTitleHelper.DefaultTitle),
                TitlePinned = !string.IsNullOrWhiteSpace(title),
                CreatedUtc = now,
                UpdatedUtc = now,
                EndpointName = endpointName ?? string.Empty,
                Model = model ?? string.Empty,
                WorkingDirectory = workingDirectory ?? string.Empty
            };

            await _Store.SaveAsync(snapshot, token).ConfigureAwait(false);
            return SessionInfo.FromSnapshot(snapshot);
        }

        /// <inheritdoc />
        public async Task<SessionInfo?> RenameAsync(string id, string title, CancellationToken token)
        {
            SessionSnapshot? snapshot = await _Store.LoadAsync(id, token).ConfigureAwait(false);
            if (snapshot == null)
            {
                return null;
            }

            snapshot.Title = SessionTitleHelper.Normalize(title, snapshot.Title);
            snapshot.TitlePinned = true;
            snapshot.UpdatedUtc = DateTime.UtcNow;

            await _Store.SaveAsync(snapshot, token).ConfigureAwait(false);
            return SessionInfo.FromSnapshot(snapshot);
        }

        /// <inheritdoc />
        public async Task<bool> SetTitlePinnedAsync(string id, bool pinned, CancellationToken token)
        {
            SessionSnapshot? snapshot = await _Store.LoadAsync(id, token).ConfigureAwait(false);
            if (snapshot == null)
            {
                return false;
            }

            snapshot.TitlePinned = pinned;
            await _Store.SaveAsync(snapshot, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<SessionInfo?> DuplicateAsync(string id, CancellationToken token)
        {
            SessionSnapshot? source = await _Store.LoadAsync(id, token).ConfigureAwait(false);
            if (source == null)
            {
                return null;
            }

            DateTime now = DateTime.UtcNow;
            SessionSnapshot copy = new SessionSnapshot
            {
                Id = NewId(),
                Title = SessionTitleHelper.Normalize(source.Title + " (copy)", SessionTitleHelper.DefaultTitle),
                TitlePinned = true,
                CreatedUtc = now,
                UpdatedUtc = now,
                EndpointName = source.EndpointName,
                Model = source.Model,
                WorkingDirectory = source.WorkingDirectory,
                CompactionCount = source.CompactionCount,
                ConversationHistory = new List<Mux.Core.Models.ConversationMessage>(source.ConversationHistory),
                PromptHistory = new List<string>(source.PromptHistory),
                Jobs = new List<PersistedJobSnapshot>(source.Jobs)
            };

            await _Store.SaveAsync(copy, token).ConfigureAwait(false);
            return SessionInfo.FromSnapshot(copy);
        }

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string id, CancellationToken token)
        {
            return _Store.DeleteAsync(id, token);
        }

        /// <inheritdoc />
        public async Task<string?> ExportAsync(string id, string format, CancellationToken token)
        {
            SessionSnapshot? snapshot = await _Store.LoadAsync(id, token).ConfigureAwait(false);
            if (snapshot == null)
            {
                return null;
            }

            return SessionExporter.Render(snapshot, format);
        }

        private static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
