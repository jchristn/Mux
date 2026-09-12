namespace Mux.Core.Prompting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Shell-style recall of previously submitted prompts, shared by both front ends. Entries are ordered
    /// oldest-to-newest. <see cref="TryPrevious"/> walks toward older entries (Up), <see cref="TryNext"/>
    /// walks back toward the newest and finally the in-progress draft (Down). The draft the user was typing
    /// is captured when navigation begins and restored when they walk past the newest entry. Consecutive
    /// duplicates are collapsed, blanks are ignored, and the list is capped at a capacity. Pure logic — no
    /// UI dependency — so both the TUI composer and the desktop composer drive the same recall behavior.
    /// </summary>
    /// <remarks>
    /// This is the single promoted implementation (previously duplicated as <c>Mux.Cli.App.PromptHistory</c>
    /// and <c>Mux.Desktop.Services.PromptHistory</c>). The desktop persists entries via its own
    /// <c>PromptHistoryStore</c> (<see cref="Load"/>/<see cref="Entries"/>); the TUI persists them inside the
    /// session snapshot (<see cref="Snapshot"/>/<see cref="Restore"/>).
    /// </remarks>
    public sealed class PromptHistory
    {
        #region Private-Members

        private readonly List<string> _Entries = new List<string>();
        private readonly int _Capacity;
        private int _Position;
        private string _Draft = string.Empty;
        private bool _Navigating;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a prompt history.
        /// </summary>
        /// <param name="capacity">The maximum number of entries kept (floored at 1). Defaults to 200.</param>
        public PromptHistory(int capacity = 200)
        {
            _Capacity = Math.Max(1, capacity);
            _Position = 0;
        }

        #endregion

        #region Public-Members

        /// <summary>The entries, oldest-to-newest.</summary>
        public IReadOnlyList<string> Entries
        {
            get => _Entries;
        }

        /// <summary>The number of stored entries.</summary>
        public int Count
        {
            get => _Entries.Count;
        }

        /// <summary>
        /// Whether the user is currently walking through history (as opposed to editing a fresh draft).
        /// </summary>
        public bool IsNavigating
        {
            get => _Navigating;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace all entries (for example loaded from disk or a resumed session), keeping only the most
        /// recent up to the capacity, and reset the cursor. Blanks are skipped and entries are trimmed.
        /// </summary>
        /// <param name="entries">The entries oldest-to-newest; null is treated as empty.</param>
        public void Load(IEnumerable<string>? entries)
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
        /// Record a submitted prompt as the newest entry (ignoring blanks and consecutive duplicates) and
        /// reset navigation to the fresh-draft position.
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
        /// Stop navigating and return to the fresh-draft position without adding anything.
        /// </summary>
        public void ResetCursor()
        {
            _Navigating = false;
            _Position = _Entries.Count;
            _Draft = string.Empty;
        }

        /// <summary>
        /// Walk toward older entries (Up). On the first call the current composer text is captured as the
        /// draft so it can be restored when the user walks back past the newest entry.
        /// </summary>
        /// <param name="current">The current composer text.</param>
        /// <param name="result">The recalled text when this returns true; the unchanged current otherwise.</param>
        /// <returns><c>true</c> when a history entry was produced; <c>false</c> when history is empty.</returns>
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
        /// Walk toward newer entries (Down), eventually restoring the captured draft when stepping past the
        /// newest entry.
        /// </summary>
        /// <param name="result">The recalled text, or the draft when walking past the newest entry.</param>
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

        /// <summary>
        /// Returns the stored entries oldest-first, for persistence (used by the TUI session snapshot).
        /// </summary>
        /// <returns>A copy of the entries.</returns>
        public IReadOnlyList<string> Snapshot()
        {
            return new List<string>(_Entries);
        }

        /// <summary>
        /// Replaces the stored entries (for example when resuming a session) and resets the cursor. Alias of
        /// <see cref="Load"/>, kept for the TUI's session-restore call site.
        /// </summary>
        /// <param name="entries">The entries to load, oldest-first. Null is treated as empty.</param>
        public void Restore(IEnumerable<string>? entries)
        {
            Load(entries);
        }

        #endregion

        #region Private-Methods

        private void Trim()
        {
            while (_Entries.Count > _Capacity)
            {
                _Entries.RemoveAt(0);
            }
        }

        #endregion
    }
}
