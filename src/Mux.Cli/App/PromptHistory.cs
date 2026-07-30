namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An in-memory recall buffer of submitted prompts with a shell-style cursor. The cursor sits past
    /// the newest entry (the "fresh draft" position); <see cref="TryPrevious"/> walks toward older
    /// entries and <see cref="TryNext"/> walks back toward the fresh draft. Consecutive duplicates are
    /// coalesced. (Cross-session persistence is a later milestone.)
    /// </summary>
    public sealed class PromptHistory
    {
        #region Private-Members

        private readonly List<string> _Entries = new List<string>();
        private int _Cursor;

        #endregion

        #region Public-Members

        /// <summary>
        /// The number of stored entries.
        /// </summary>
        public int Count
        {
            get => _Entries.Count;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Adds a submitted prompt (ignoring blanks and consecutive duplicates) and resets the cursor to
        /// the fresh-draft position.
        /// </summary>
        /// <param name="prompt">The prompt that was submitted.</param>
        public void Add(string prompt)
        {
            if (!string.IsNullOrWhiteSpace(prompt)
                && (_Entries.Count == 0 || !string.Equals(_Entries[_Entries.Count - 1], prompt, StringComparison.Ordinal)))
            {
                _Entries.Add(prompt);
            }

            _Cursor = _Entries.Count;
        }

        /// <summary>
        /// Moves the cursor toward older entries.
        /// </summary>
        /// <param name="entry">The recalled entry when this returns true.</param>
        /// <returns>True when an entry is available; false when history is empty.</returns>
        public bool TryPrevious(out string entry)
        {
            if (_Entries.Count == 0)
            {
                entry = string.Empty;
                return false;
            }

            if (_Cursor > 0)
            {
                _Cursor--;
            }

            entry = _Entries[_Cursor];
            return true;
        }

        /// <summary>
        /// Moves the cursor toward newer entries; stepping past the newest returns the empty draft.
        /// </summary>
        /// <param name="entry">The recalled entry, or empty when returning to the fresh draft.</param>
        /// <returns>True when the cursor moved; false when already at the fresh draft.</returns>
        public bool TryNext(out string entry)
        {
            if (_Cursor >= _Entries.Count)
            {
                entry = string.Empty;
                return false;
            }

            _Cursor++;
            entry = _Cursor >= _Entries.Count ? string.Empty : _Entries[_Cursor];
            return true;
        }

        /// <summary>
        /// Resets the cursor to the fresh-draft position without adding anything.
        /// </summary>
        public void ResetCursor()
        {
            _Cursor = _Entries.Count;
        }

        #endregion
    }
}
