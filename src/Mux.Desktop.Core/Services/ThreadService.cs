namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;

    /// <summary>
    /// Default <see cref="IThreadService"/> over a <see cref="SessionStore"/>. Threads are persisted
    /// sessions; creation, rename, pin, duplicate, delete, and export map onto the store and the shared
    /// session helpers. Thread ids are opaque GUIDs so they are always valid file names.
    /// </summary>
    public sealed class ThreadService : IThreadService
    {
        private readonly SessionStore _Store;

        /// <summary>
        /// Instantiate the thread service.
        /// </summary>
        /// <param name="store">The session store backing the threads. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public ThreadService(SessionStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            _Store = store;
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<ThreadSummary>> ListAsync(CancellationToken token)
        {
            IReadOnlyList<SessionSnapshot> snapshots = await _Store.ListAsync(token).ConfigureAwait(false);

            List<ThreadSummary> summaries = new List<ThreadSummary>(snapshots.Count);
            foreach (SessionSnapshot snapshot in snapshots)
            {
                summaries.Add(MapToSummary(snapshot));
            }

            return summaries
                .OrderByDescending(summary => summary.UpdatedUtc)
                .ToList();
        }

        /// <inheritdoc />
        public async Task<ThreadSummary> CreateAsync(string? title, string? endpointName, string? model, CancellationToken token)
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
                Model = model ?? string.Empty
            };

            await _Store.SaveAsync(snapshot, token).ConfigureAwait(false);
            return MapToSummary(snapshot);
        }

        /// <inheritdoc />
        public async Task<ThreadSummary?> RenameAsync(string id, string title, CancellationToken token)
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
            return MapToSummary(snapshot);
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
        public async Task<ThreadSummary?> DuplicateAsync(string id, CancellationToken token)
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
                CompactionCount = source.CompactionCount,
                ConversationHistory = new List<Mux.Core.Models.ConversationMessage>(source.ConversationHistory),
                PromptHistory = new List<string>(source.PromptHistory),
                Jobs = new List<PersistedJobSnapshot>(source.Jobs)
            };

            await _Store.SaveAsync(copy, token).ConfigureAwait(false);
            return MapToSummary(copy);
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

        private static ThreadSummary MapToSummary(SessionSnapshot snapshot)
        {
            return new ThreadSummary(
                snapshot.Id,
                snapshot.Title,
                snapshot.EndpointName,
                snapshot.Model,
                snapshot.CreatedUtc,
                snapshot.UpdatedUtc,
                snapshot.ConversationHistory.Count,
                snapshot.TitlePinned);
        }
    }
}
