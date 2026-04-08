namespace Mux.Cli.Rendering
{
    using System.Collections.Generic;

    /// <summary>
    /// Tracks submitted prompts for the current interactive session and provides
    /// shell-style previous/next navigation through that history.
    /// </summary>
    public class PromptHistory
    {
        #region Private-Members

        private readonly List<string> _Entries = new List<string>();
        private int _BrowseIndex = -1;
        private string _Draft = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether the history is currently showing a previously submitted prompt.
        /// </summary>
        public bool IsBrowsing
        {
            get => _BrowseIndex >= 0;
        }

        /// <summary>
        /// The number of stored prompts for this session.
        /// </summary>
        public int Count
        {
            get => _Entries.Count;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Adds a submitted prompt to session history.
        /// </summary>
        /// <param name="prompt">The submitted prompt text.</param>
        public void Add(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _Entries.Add(prompt);
            ResetNavigation();
        }

        /// <summary>
        /// Resets any active history navigation state.
        /// </summary>
        public void ResetNavigation()
        {
            _BrowseIndex = -1;
            _Draft = string.Empty;
        }

        /// <summary>
        /// Moves backward through history and returns the selected prompt.
        /// </summary>
        /// <param name="currentText">The current in-progress draft before browsing begins.</param>
        /// <param name="prompt">The recalled prompt text.</param>
        /// <returns>True if a prompt was recalled; otherwise false.</returns>
        public bool TryMovePrevious(string currentText, out string prompt)
        {
            prompt = currentText;

            if (_Entries.Count == 0)
            {
                return false;
            }

            if (_BrowseIndex < 0)
            {
                _Draft = currentText;
                _BrowseIndex = _Entries.Count - 1;
            }
            else if (_BrowseIndex > 0)
            {
                _BrowseIndex--;
            }

            prompt = _Entries[_BrowseIndex];
            return true;
        }

        /// <summary>
        /// Moves forward through history and returns the selected prompt or the saved draft.
        /// </summary>
        /// <param name="prompt">The next prompt text or restored draft.</param>
        /// <returns>True if navigation occurred; otherwise false.</returns>
        public bool TryMoveNext(out string prompt)
        {
            prompt = string.Empty;

            if (_BrowseIndex < 0)
            {
                return false;
            }

            if (_BrowseIndex < _Entries.Count - 1)
            {
                _BrowseIndex++;
                prompt = _Entries[_BrowseIndex];
                return true;
            }

            _BrowseIndex = -1;
            prompt = _Draft;
            _Draft = string.Empty;
            return true;
        }

        #endregion
    }
}
