namespace Mux.Core.Checkpoints
{
    using System;

    /// <summary>
    /// One captured workspace snapshot: the git object id of the dangling snapshot commit and the label it
    /// was taken under. Immutable; produced by <see cref="CheckpointManager"/>.
    /// </summary>
    public sealed class Checkpoint
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="Checkpoint"/> class.
        /// </summary>
        /// <param name="sha">The snapshot commit id. Must not be null or empty.</param>
        /// <param name="label">The human-readable label. Null becomes empty.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="sha"/> is null or empty.</exception>
        public Checkpoint(string sha, string? label)
        {
            if (string.IsNullOrWhiteSpace(sha))
            {
                throw new ArgumentException("Checkpoint SHA cannot be null or empty.", nameof(sha));
            }

            Sha = sha;
            Label = label ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The git object id of the snapshot commit.
        /// </summary>
        public string Sha { get; }

        /// <summary>
        /// The human-readable label the snapshot was captured under.
        /// </summary>
        public string Label { get; }

        #endregion
    }
}
