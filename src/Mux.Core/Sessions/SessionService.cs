namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The single, surface-agnostic entry point for reading and persisting sessions. Every mux surface (the
    /// terminal, the desktop app, and the REST/WebSocket server that backs the dashboard and the VS Code
    /// extension) goes through one instance so they all share identical semantics: opening a session always
    /// reflects the store, and persisting a turn reconciles against the store (via <see cref="SessionMergePolicy"/>)
    /// so no surface can truncate a conversation another surface extended, or drop cross-surface fields it does
    /// not itself author.
    ///
    /// <para>It is a thin coordinator over <see cref="SessionStore"/>; it holds no cache, so a read always sees
    /// the latest bytes on disk.</para>
    /// </summary>
    public sealed class SessionService
    {
        #region Private-Members

        private readonly SessionStore _Store;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="store">The backing session store. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public SessionService(SessionStore store)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        #endregion

        #region Public-Members

        /// <summary>The backing store's root directory (for a caller that watches it directly).</summary>
        public string RootDirectory
        {
            get => _Store.RootDirectory;
        }

        /// <summary>The backing session store.</summary>
        public SessionStore Store
        {
            get => _Store;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Loads a session's full, current snapshot from the store. This is the canonical "open" path: a surface
        /// showing a session must call it so the transcript reflects every turn made anywhere, not a stale view.
        /// </summary>
        /// <param name="id">The session id.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The snapshot, or null when no session with that id exists.</returns>
        public Task<SessionSnapshot?> LoadAsync(string id, CancellationToken cancellationToken)
        {
            return _Store.LoadAsync(id, cancellationToken);
        }

        /// <summary>
        /// Loads every persisted session snapshot (for a conversation list).
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The snapshots.</returns>
        public Task<IReadOnlyList<SessionSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            return _Store.ListAsync(cancellationToken);
        }

        /// <summary>
        /// Persists a session after a turn, reconciling against whatever is already in the store so a save can
        /// never truncate a conversation (see <see cref="SessionMergePolicy"/>) and cross-surface fields the
        /// caller did not author (created time, compaction count, jobs, prompt history, a pinned title, and the
        /// portability fields) are preserved from the stored copy. Mutates and returns <paramref name="incoming"/>
        /// as the authoritative persisted snapshot.
        /// </summary>
        /// <param name="incoming">The snapshot the surface wants to persist. Its <see cref="SessionSnapshot.Id"/>
        /// must be set. Required.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The persisted snapshot (the same instance as <paramref name="incoming"/>).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="incoming"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="incoming"/> has no id.</exception>
        public async Task<SessionSnapshot> PersistConversationAsync(SessionSnapshot incoming, CancellationToken cancellationToken)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));
            if (string.IsNullOrWhiteSpace(incoming.Id)) throw new ArgumentException("Session id is required.", nameof(incoming));

            SessionSnapshot? existing = null;
            try
            {
                existing = await _Store.LoadAsync(incoming.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // A corrupt/unreadable store copy must not block the write; treat as no prior state.
                existing = null;
            }

            incoming.ConversationHistory = SessionMergePolicy.Reconcile(existing?.ConversationHistory, incoming.ConversationHistory);

            if (existing != null)
            {
                PreserveUnauthoredFields(incoming, existing);
            }

            await _Store.SaveAsync(incoming, cancellationToken).ConfigureAwait(false);
            return incoming;
        }

        #endregion

        #region Private-Methods

        // Fill fields the persisting surface may not track from the stored copy, so a round-trip through a surface
        // that only knows about the conversation itself does not erase state authored elsewhere.
        private static void PreserveUnauthoredFields(SessionSnapshot incoming, SessionSnapshot existing)
        {
            if (incoming.CreatedUtc == default)
            {
                incoming.CreatedUtc = existing.CreatedUtc;
            }

            if (string.IsNullOrEmpty(incoming.Title) && !string.IsNullOrEmpty(existing.Title))
            {
                incoming.Title = existing.Title;
            }

            // A pin set on any surface should survive a save from a surface that does not track pinning.
            incoming.TitlePinned = incoming.TitlePinned || existing.TitlePinned;

            if (existing.CompactionCount > incoming.CompactionCount)
            {
                incoming.CompactionCount = existing.CompactionCount;
            }

            if (string.IsNullOrEmpty(incoming.EndpointName) && !string.IsNullOrEmpty(existing.EndpointName))
            {
                incoming.EndpointName = existing.EndpointName;
            }

            if (string.IsNullOrEmpty(incoming.Model) && !string.IsNullOrEmpty(existing.Model))
            {
                incoming.Model = existing.Model;
            }

            if (string.IsNullOrEmpty(incoming.WorkingDirectory) && !string.IsNullOrEmpty(existing.WorkingDirectory))
            {
                incoming.WorkingDirectory = existing.WorkingDirectory;
            }

            if (incoming.Jobs.Count == 0 && existing.Jobs.Count > 0)
            {
                incoming.Jobs = existing.Jobs;
            }

            if (incoming.PromptHistory.Count == 0 && existing.PromptHistory.Count > 0)
            {
                incoming.PromptHistory = existing.PromptHistory;
            }
        }

        #endregion
    }
}
