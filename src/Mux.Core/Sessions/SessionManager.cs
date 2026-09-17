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
        public Task<SessionInfo?> AddLabelAsync(string id, string label, CancellationToken token)
        {
            if (!SessionMetadataNormalizer.TryNormalizeLabel(label, out string normalized, out string? error))
            {
                throw new ArgumentException(error, nameof(label));
            }

            return MutateAsync(id, snapshot =>
            {
                bool exists = snapshot.Labels.Exists(l => string.Equals(l, normalized, StringComparison.OrdinalIgnoreCase));
                if (!exists)
                {
                    snapshot.Labels.Add(normalized);
                    return true;
                }

                return false;
            }, token);
        }

        /// <inheritdoc />
        public Task<SessionInfo?> RemoveLabelAsync(string id, string label, CancellationToken token)
        {
            // Removal is lenient: match on the raw and normalized forms so a user can remove what they see.
            string normalized = SessionMetadataNormalizer.TryNormalizeLabel(label, out string n, out _) ? n : (label ?? string.Empty).Trim();

            return MutateAsync(id, snapshot =>
            {
                int removed = snapshot.Labels.RemoveAll(l =>
                    string.Equals(l, normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(l, (label ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
                return removed > 0;
            }, token);
        }

        /// <inheritdoc />
        public Task<SessionInfo?> SetTagAsync(string id, string key, string value, CancellationToken token)
        {
            if (!SessionMetadataNormalizer.TryNormalizeTagKey(key, out string normalizedKey, out string? keyError))
            {
                throw new ArgumentException(keyError, nameof(key));
            }

            if (!SessionMetadataNormalizer.TryNormalizeTagValue(value, out string normalizedValue, out string? valueError))
            {
                throw new ArgumentException(valueError, nameof(value));
            }

            return MutateAsync(id, snapshot =>
            {
                SessionTag? existing = snapshot.Tags.Find(t => string.Equals(t.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (string.Equals(existing.Value, normalizedValue, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    existing.Value = normalizedValue;
                    return true;
                }

                snapshot.Tags.Add(new SessionTag(normalizedKey, normalizedValue));
                return true;
            }, token);
        }

        /// <inheritdoc />
        public Task<SessionInfo?> RemoveTagAsync(string id, string key, CancellationToken token)
        {
            if (!SessionMetadataNormalizer.TryNormalizeTagKey(key, out string normalizedKey, out string? keyError))
            {
                throw new ArgumentException(keyError, nameof(key));
            }

            return MutateAsync(id, snapshot =>
            {
                int removed = snapshot.Tags.RemoveAll(t => string.Equals(t.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
                return removed > 0;
            }, token);
        }

        /// <inheritdoc />
        public Task<SessionInfo?> SetMetadataAsync(string id, IEnumerable<string>? labels, IEnumerable<SessionTag>? tags, CancellationToken token)
        {
            return MutateAsync(id, snapshot =>
            {
                bool changed = false;

                if (labels != null)
                {
                    foreach (string raw in labels)
                    {
                        if (!SessionMetadataNormalizer.TryNormalizeLabel(raw, out string normalized, out _))
                        {
                            continue;
                        }

                        if (!snapshot.Labels.Exists(l => string.Equals(l, normalized, StringComparison.OrdinalIgnoreCase)))
                        {
                            snapshot.Labels.Add(normalized);
                            changed = true;
                        }
                    }
                }

                if (tags != null)
                {
                    foreach (SessionTag raw in tags)
                    {
                        if (raw == null) continue;
                        if (!SessionMetadataNormalizer.TryNormalizeTagKey(raw.Key, out string normalizedKey, out _)) continue;
                        if (!SessionMetadataNormalizer.TryNormalizeTagValue(raw.Value, out string normalizedValue, out _)) continue;

                        SessionTag? existing = snapshot.Tags.Find(t => string.Equals(t.Key, normalizedKey, StringComparison.OrdinalIgnoreCase));
                        if (existing != null)
                        {
                            if (!string.Equals(existing.Value, normalizedValue, StringComparison.Ordinal))
                            {
                                existing.Value = normalizedValue;
                                changed = true;
                            }
                        }
                        else
                        {
                            snapshot.Tags.Add(new SessionTag(normalizedKey, normalizedValue));
                            changed = true;
                        }
                    }
                }

                return changed;
            }, token);
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
                Labels = new List<string>(source.Labels),
                Tags = source.Tags.Select(t => new SessionTag(t.Key, t.Value)).ToList(),
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

        // Load → apply → (save when the mutation reported a change) → project. Returns null when the session is
        // missing; when the mutation is a no-op the stored UpdatedUtc is left untouched so idempotent calls do
        // not churn the session's ordering in list views.
        private async Task<SessionInfo?> MutateAsync(string id, Func<SessionSnapshot, bool> mutate, CancellationToken token)
        {
            SessionSnapshot? snapshot = await _Store.LoadAsync(id, token).ConfigureAwait(false);
            if (snapshot == null)
            {
                return null;
            }

            bool changed = mutate(snapshot);
            if (changed)
            {
                snapshot.UpdatedUtc = DateTime.UtcNow;
                await _Store.SaveAsync(snapshot, token).ConfigureAwait(false);
            }

            return SessionInfo.FromSnapshot(snapshot);
        }

        private static string NewId()
        {
            return Guid.NewGuid().ToString("N");
        }
    }
}
