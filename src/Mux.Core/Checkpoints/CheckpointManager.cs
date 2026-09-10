namespace Mux.Core.Checkpoints
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Maintains per-turn undo/redo history over a <see cref="GitCheckpointService"/>. Before a turn runs the
    /// caller records a checkpoint of the pre-turn workspace; the live working tree is the implicit "current"
    /// state. <see cref="UndoAsync"/> captures the current state onto the redo stack and restores the most
    /// recent recorded checkpoint; <see cref="RedoAsync"/> is the inverse. Recording a new checkpoint clears
    /// the redo stack, matching standard editor undo semantics.
    /// <para>
    /// All operations serialize on an internal gate so a checkpoint recorded mid-undo cannot corrupt the
    /// stacks. Callers should still avoid recording a checkpoint while a turn is mutating files.
    /// </para>
    /// </summary>
    public sealed class CheckpointManager
    {
        #region Private-Members

        private readonly GitCheckpointService _Service;
        private readonly Stack<Checkpoint> _Undo = new Stack<Checkpoint>();
        private readonly Stack<Checkpoint> _Redo = new Stack<Checkpoint>();
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckpointManager"/> class.
        /// </summary>
        /// <param name="service">The git checkpoint service to capture and restore snapshots. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="service"/> is null.</exception>
        public CheckpointManager(GitCheckpointService service)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether at least one checkpoint is available to undo to.
        /// </summary>
        public bool CanUndo
        {
            get
            {
                lock (_Undo)
                {
                    return _Undo.Count > 0;
                }
            }
        }

        /// <summary>
        /// Whether at least one undone checkpoint is available to redo.
        /// </summary>
        public bool CanRedo
        {
            get
            {
                lock (_Redo)
                {
                    return _Redo.Count > 0;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Records a checkpoint of the current workspace under the given label and clears the redo history.
        /// Call this immediately before a turn that may modify files.
        /// </summary>
        /// <param name="label">A human-readable label for the checkpoint.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The recorded checkpoint.</returns>
        public async Task<Checkpoint> RecordAsync(string label, CancellationToken cancellationToken)
        {
            await _Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                string sha = await _Service.CaptureAsync(label, cancellationToken).ConfigureAwait(false);
                Checkpoint checkpoint = new Checkpoint(sha, label);
                lock (_Undo)
                {
                    _Undo.Push(checkpoint);
                }

                lock (_Redo)
                {
                    _Redo.Clear();
                }

                return checkpoint;
            }
            finally
            {
                _Gate.Release();
            }
        }

        /// <summary>
        /// Restores the most recently recorded checkpoint, moving the current state onto the redo stack.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The checkpoint that was restored, or null when there is nothing to undo.</returns>
        public async Task<Checkpoint?> UndoAsync(CancellationToken cancellationToken)
        {
            await _Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Checkpoint target;
                lock (_Undo)
                {
                    if (_Undo.Count == 0)
                    {
                        return null;
                    }

                    target = _Undo.Peek();
                }

                // Capture the current (pre-undo) state so the undo can be redone, then restore the target.
                string currentSha = await _Service.CaptureAsync("before undo", cancellationToken).ConfigureAwait(false);
                await _Service.RestoreAsync(target.Sha, cancellationToken).ConfigureAwait(false);

                lock (_Undo)
                {
                    _Undo.Pop();
                }

                lock (_Redo)
                {
                    _Redo.Push(new Checkpoint(currentSha, target.Label));
                }

                return target;
            }
            finally
            {
                _Gate.Release();
            }
        }

        /// <summary>
        /// Re-applies the most recently undone checkpoint, moving the current state back onto the undo stack.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The checkpoint that was restored, or null when there is nothing to redo.</returns>
        public async Task<Checkpoint?> RedoAsync(CancellationToken cancellationToken)
        {
            await _Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Checkpoint target;
                lock (_Redo)
                {
                    if (_Redo.Count == 0)
                    {
                        return null;
                    }

                    target = _Redo.Peek();
                }

                string currentSha = await _Service.CaptureAsync("before redo", cancellationToken).ConfigureAwait(false);
                await _Service.RestoreAsync(target.Sha, cancellationToken).ConfigureAwait(false);

                lock (_Redo)
                {
                    _Redo.Pop();
                }

                lock (_Undo)
                {
                    _Undo.Push(new Checkpoint(currentSha, target.Label));
                }

                return target;
            }
            finally
            {
                _Gate.Release();
            }
        }

        #endregion
    }
}
