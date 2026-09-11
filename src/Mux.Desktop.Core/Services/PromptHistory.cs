namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Shell-style recall of previously submitted prompts. Entries are ordered oldest-to-newest.
    /// <see cref="TryPrevious"/> walks toward older entries (Up), <see cref="TryNext"/> walks back toward the
    /// newest and finally the in-progress draft (Down). The draft the user was typing is preserved when
    /// navigation begins and restored when they walk past the newest entry. Consecutive duplicates are
    /// collapsed and the list is capped.
    /// </summary>
    public sealed class PromptHistory
    {
        private readonly List<string> _Entries = new List<string>();
        private readonly int _Capacity;
        private int _Position;
        private string _Draft = string.Empty;
        private bool _Navigating;

        /// <summary>
        /// Instantiate a prompt history.
        /// </summary>
        /// <param name="capacity">The maximum number of entries kept (floored at 1). Defaults to 200.</param>
        public PromptHistory(int capacity = 200)
        {
            _Capacity = Math.Max(1, capacity);
            _Position = 0;
        }

        /// <summary>
        /// The entries, oldest-to-newest.
        /// </summary>
        public IReadOnlyList<string> Entries => _Entries;

        /// <summary>
        /// Whether the user is currently walking through history (as opposed to editing a fresh draft).
        /// </summary>
        public bool IsNavigating => _Navigating;

        /// <summary>
        /// Replace all entries (e.g. loaded from disk), keeping only the most recent up to the capacity.
        /// </summary>
        /// <param name="entries">The entries oldest-to-newest; null is treated as empty.</param>
        public void Load(IEnumerable<string> entries)
        {
            _Entries.Clear();
            if (entries != null)
            {
                foreach (string entry in entries)
                {
                    string trimmed = (entry ?? string.Empty).Trim();
                    if (trimmed.Length > 0)
                    {
                        _Entries.Add(trimmed);
                    }
                }
            }

            Trim();
            ResetCursor();
        }

        /// <summary>
        /// Record a submitted prompt as the newest entry and reset navigation.
        /// </summary>
        /// <param name="prompt">The submitted prompt.</param>
        public void Add(string prompt)
        {
            string trimmed = (prompt ?? string.Empty).Trim();
            ResetCursor();
            if (trimmed.Length == 0)
            {
                return;
            }

            if (_Entries.Count > 0 && string.Equals(_Entries[_Entries.Count - 1], trimmed, StringComparison.Ordinal))
            {
                return;
            }

            _Entries.Add(trimmed);
            Trim();
            ResetCursor();
        }

        /// <summary>
        /// Stop navigating and return to the draft position.
        /// </summary>
        public void ResetCursor()
        {
            _Navigating = false;
            _Position = _Entries.Count;
            _Draft = string.Empty;
        }

        /// <summary>
        /// Walk toward older entries (Up). On the first call the current text is captured as the draft.
        /// </summary>
        /// <param name="current">The current composer text.</param>
        /// <param name="result">The recalled text.</param>
        /// <returns><c>true</c> when a history entry was produced; otherwise <c>false</c> (empty history).</returns>
        public bool TryPrevious(string current, out string result)
        {
            result = current ?? string.Empty;
            if (_Entries.Count == 0)
            {
                return false;
            }

            if (!_Navigating)
            {
                _Navigating = true;
                _Draft = current ?? string.Empty;
                _Position = _Entries.Count;
            }

            if (_Position > 0)
            {
                _Position--;
            }

            result = _Entries[_Position];
            return true;
        }

        /// <summary>
        /// Walk toward newer entries (Down), eventually restoring the draft.
        /// </summary>
        /// <param name="result">The recalled text (or the draft when walking past the newest entry).</param>
        /// <returns><c>true</c> when navigating; <c>false</c> when there was nothing to do.</returns>
        public bool TryNext(out string result)
        {
            result = string.Empty;
            if (!_Navigating)
            {
                return false;
            }

            if (_Position >= _Entries.Count - 1)
            {
                _Position = _Entries.Count;
                _Navigating = false;
                result = _Draft;
                return true;
            }

            _Position++;
            result = _Entries[_Position];
            return true;
        }

        private void Trim()
        {
            while (_Entries.Count > _Capacity)
            {
                _Entries.RemoveAt(0);
            }
        }
    }
}
