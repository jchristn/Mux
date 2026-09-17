namespace Mux.Core.Telemetry
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;

    /// <summary>
    /// <see cref="ISessionMetadataIndex"/> backed by the shared <see cref="SessionStore"/>. Projects every
    /// persisted session's labels/tags on demand. Session counts are small and files are tiny, so it re-reads
    /// the store per query rather than maintaining a cache; correctness (a relabel is visible immediately)
    /// wins over shaving a few milliseconds. A future optimization could cache against a store change token.
    /// </summary>
    public sealed class SessionStoreMetadataIndex : ISessionMetadataIndex
    {
        #region Private-Members

        private readonly SessionStore _Store;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionStoreMetadataIndex"/> class.
        /// </summary>
        /// <param name="store">The session store to project. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public SessionStoreMetadataIndex(SessionStore store)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<IReadOnlyList<SessionMetadataEntry>> SnapshotAsync(CancellationToken token)
        {
            IReadOnlyList<SessionSnapshot> snapshots = await _Store.ListAsync(token).ConfigureAwait(false);
            List<SessionMetadataEntry> entries = new List<SessionMetadataEntry>(snapshots.Count);

            foreach (SessionSnapshot snapshot in snapshots)
            {
                if (snapshot.Labels.Count == 0 && snapshot.Tags.Count == 0)
                {
                    // Sessions with no metadata cannot match any label/tag filter and contribute nothing to a
                    // label/tag breakdown, so they are omitted to keep the projection lean.
                    continue;
                }

                entries.Add(new SessionMetadataEntry
                {
                    Id = snapshot.Id,
                    Labels = snapshot.Labels.ToList(),
                    Tags = snapshot.Tags.Select(t => new SessionTag(t.Key, t.Value)).ToList()
                });
            }

            return entries;
        }

        #endregion
    }
}
