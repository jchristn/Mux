namespace Mux.Core.Sessions
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Persists <see cref="SessionSnapshot"/> instances as one JSON file per session under a
    /// configurable root directory. Writes are atomic (temp file + move) so an interrupted save can
    /// never leave a half-written session file, and reads are forward-tolerant of unknown fields.
    /// All members are thread-safe with respect to the file system; concurrent saves to the same id
    /// are the caller's responsibility.
    /// </summary>
    public sealed class SessionStore
    {
        #region Private-Members

        private readonly JsonSerializerOptions _Options;
        private string _RootDirectory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SessionStore"/> class.
        /// </summary>
        /// <param name="rootDirectory">
        /// The directory that holds session files. When null or empty, defaults to
        /// <c>&lt;user profile&gt;/.mux/sessions</c>.
        /// </param>
        public SessionStore(string? rootDirectory = null)
        {
            _RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mux", "sessions")
                : rootDirectory;

            _Options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The directory that holds session files. Setting null or empty restores the default location.
        /// </summary>
        public string RootDirectory
        {
            get => _RootDirectory;
            set => _RootDirectory = string.IsNullOrWhiteSpace(value)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mux", "sessions")
                : value;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Saves a session snapshot atomically. Creates the root directory if needed.
        /// </summary>
        /// <param name="snapshot">The snapshot to save. Must not be null and must have a valid id.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task that completes when the file has been written and moved into place.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="snapshot"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the snapshot id is empty or not a valid file name.</exception>
        public async Task SaveAsync(SessionSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            string path = ResolvePath(snapshot.Id);
            Directory.CreateDirectory(_RootDirectory);

            string json = JsonSerializer.Serialize(snapshot, _Options);
            string tempPath = path + ".tmp";

            await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }

        /// <summary>
        /// Loads a session snapshot by id.
        /// </summary>
        /// <param name="id">The session id.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The snapshot, or null when no session with that id exists.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty or not a valid file name.</exception>
        public async Task<SessionSnapshot?> LoadAsync(string id, CancellationToken cancellationToken)
        {
            string path = ResolvePath(id);
            if (!File.Exists(path))
            {
                return null;
            }

            string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<SessionSnapshot>(json, _Options);
        }

        /// <summary>
        /// Lists the ids of all persisted sessions (files with a <c>.json</c> extension).
        /// </summary>
        /// <returns>The session ids, or an empty list when the root directory does not exist.</returns>
        public IReadOnlyList<string> ListSessionIds()
        {
            List<string> ids = new List<string>();
            if (!Directory.Exists(_RootDirectory))
            {
                return ids;
            }

            foreach (string path in Directory.EnumerateFiles(_RootDirectory, "*.json"))
            {
                ids.Add(Path.GetFileNameWithoutExtension(path));
            }

            return ids;
        }

        /// <summary>
        /// Asynchronous variant of <see cref="ListSessionIds"/>.
        /// </summary>
        /// <param name="cancellationToken">A token to observe for cancellation.</param>
        /// <returns>The session ids.</returns>
        public Task<IReadOnlyList<string>> ListSessionIdsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ListSessionIds());
        }

        /// <summary>
        /// Loads every persisted session snapshot. Files that fail to deserialize are skipped.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The loaded snapshots.</returns>
        public async Task<IReadOnlyList<SessionSnapshot>> ListAsync(CancellationToken cancellationToken)
        {
            List<SessionSnapshot> snapshots = new List<SessionSnapshot>();
            foreach (string id in ListSessionIds())
            {
                cancellationToken.ThrowIfCancellationRequested();
                SessionSnapshot? snapshot = await LoadAsync(id, cancellationToken).ConfigureAwait(false);
                if (snapshot != null)
                {
                    snapshots.Add(snapshot);
                }
            }

            return snapshots;
        }

        /// <summary>
        /// Deletes a session by id.
        /// </summary>
        /// <param name="id">The session id.</param>
        /// <param name="cancellationToken">A token to observe for cancellation.</param>
        /// <returns>True when a file was deleted; false when none existed.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is empty or not a valid file name.</exception>
        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = ResolvePath(id);
            if (!File.Exists(path))
            {
                return Task.FromResult(false);
            }

            File.Delete(path);
            return Task.FromResult(true);
        }

        /// <summary>
        /// Duplicates an existing session under a new id.
        /// </summary>
        /// <param name="sourceId">The id of the session to copy.</param>
        /// <param name="newId">The id for the copy.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The saved duplicate snapshot, or null when the source does not exist.</returns>
        /// <exception cref="ArgumentException">Thrown when either id is empty or not a valid file name.</exception>
        public async Task<SessionSnapshot?> DuplicateAsync(string sourceId, string newId, CancellationToken cancellationToken)
        {
            SessionSnapshot? source = await LoadAsync(sourceId, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return null;
            }

            source.Id = newId;
            await SaveAsync(source, cancellationToken).ConfigureAwait(false);
            return source;
        }

        #endregion

        #region Private-Methods

        private string ResolvePath(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Session id cannot be null or empty.", nameof(id));

            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || id.Contains(Path.DirectorySeparatorChar)
                || id.Contains(Path.AltDirectorySeparatorChar))
            {
                throw new ArgumentException($"Session id '{id}' is not a valid file name.", nameof(id));
            }

            return Path.Combine(_RootDirectory, id + ".json");
        }

        #endregion
    }
}
