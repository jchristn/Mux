namespace Mux.Server
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Checkpoints;

    /// <summary>
    /// Holds one <see cref="CheckpointManager"/> per working directory for the life of the server, so a run's
    /// pre-turn snapshot and a later undo/redo request — which arrive as separate HTTP calls — share the same
    /// undo/redo history. A directory that is not a git work tree resolves to a null manager (cached), so the
    /// probe runs once and undo/redo report themselves unavailable rather than erroring. Checkpoints are git
    /// shadow refs; they never touch the user's branch, history, or stash.
    /// </summary>
    public sealed class CheckpointRegistry
    {
        private readonly ConcurrentDictionary<string, CheckpointManager?> _ByDirectory =
            new ConcurrentDictionary<string, CheckpointManager?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the checkpoint manager for a directory, creating it on first use, or null when the
        /// directory is not a git repository. Thread-safe; the git probe runs at most once per directory.
        /// </summary>
        /// <param name="workingDirectory">The directory to snapshot against.</param>
        /// <param name="cancellationToken">A token to cancel the git probe.</param>
        /// <returns>The manager, or null when the directory is not a repository.</returns>
        public async Task<CheckpointManager?> GetOrCreateAsync(string workingDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return null;
            }

            string key = Normalize(workingDirectory);
            if (_ByDirectory.TryGetValue(key, out CheckpointManager? existing))
            {
                return existing;
            }

            CheckpointManager? manager = null;
            try
            {
                GitCheckpointService service = new GitCheckpointService(key);
                if (await service.IsRepositoryAsync(cancellationToken).ConfigureAwait(false))
                {
                    manager = new CheckpointManager(service);
                }
            }
            catch (Exception)
            {
                manager = null;
            }

            // Cache the result (including null) so a non-repo directory is probed once.
            return _ByDirectory.GetOrAdd(key, manager);
        }

        private static string Normalize(string directory)
        {
            try
            {
                return Path.GetFullPath(directory);
            }
            catch (Exception)
            {
                return directory;
            }
        }
    }
}
