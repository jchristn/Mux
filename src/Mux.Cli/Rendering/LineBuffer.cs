namespace Mux.Cli.Rendering
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// A line-editable text buffer that tracks cursor position within the current line.
    /// Supports insert, delete, and cursor movement (left, right, home, end) operations.
    /// This class manages the logical buffer only — console rendering is the caller's responsibility.
    /// </summary>
    public class LineBuffer
    {
        #region Private-Members

        private List<string> _Lines = new List<string> { string.Empty };
        private int _CurrentLineIndex = 0;
        private int _CursorColumn = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// The zero-based index of the current line.
        /// </summary>
        public int CurrentLineIndex
        {
            get => _CurrentLineIndex;
        }

        /// <summary>
        /// The zero-based cursor column within the current line.
        /// </summary>
        public int CursorColumn
        {
            get => _CursorColumn;
        }

        /// <summary>
        /// The total number of lines in the buffer.
        /// </summary>
        public int LineCount
        {
            get => _Lines.Count;
        }

        /// <summary>
        /// The text of the current line.
        /// </summary>
        public string CurrentLine
        {
            get => _Lines[_CurrentLineIndex];
        }

        /// <summary>
        /// The text to the right of the cursor on the current line (including the character at the cursor).
        /// </summary>
        public string TextAfterCursor
        {
            get => _Lines[_CurrentLineIndex].Substring(_CursorColumn);
        }

        /// <summary>
        /// Whether the cursor is at the end of the current line.
        /// </summary>
        public bool IsCursorAtEnd
        {
            get => _CursorColumn >= _Lines[_CurrentLineIndex].Length;
        }

        /// <summary>
        /// Whether the cursor is at the start of the current line.
        /// </summary>
        public bool IsCursorAtStart
        {
            get => _CursorColumn == 0;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Inserts a character at the current cursor position and advances the cursor.
        /// </summary>
        /// <param name="c">The character to insert.</param>
        public void Insert(char c)
        {
            string line = _Lines[_CurrentLineIndex];
            _Lines[_CurrentLineIndex] = line.Insert(_CursorColumn, c.ToString());
            _CursorColumn++;
        }

        /// <summary>
        /// Deletes the character before the cursor (backspace).
        /// </summary>
        /// <returns>True if a character was deleted; false if the cursor was already at position 0.</returns>
        public bool Backspace()
        {
            if (_CursorColumn > 0)
            {
                string line = _Lines[_CurrentLineIndex];
                _Lines[_CurrentLineIndex] = line.Remove(_CursorColumn - 1, 1);
                _CursorColumn--;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Deletes the character at the cursor position (forward delete).
        /// </summary>
        /// <returns>True if a character was deleted; false if the cursor was already at end of line.</returns>
        public bool Delete()
        {
            string line = _Lines[_CurrentLineIndex];
            if (_CursorColumn < line.Length)
            {
                _Lines[_CurrentLineIndex] = line.Remove(_CursorColumn, 1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves the cursor one position to the left.
        /// </summary>
        /// <returns>True if the cursor moved; false if already at position 0.</returns>
        public bool MoveLeft()
        {
            if (_CursorColumn > 0)
            {
                _CursorColumn--;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves the cursor one position to the right.
        /// </summary>
        /// <returns>True if the cursor moved; false if already at end of line.</returns>
        public bool MoveRight()
        {
            if (_CursorColumn < _Lines[_CurrentLineIndex].Length)
            {
                _CursorColumn++;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves the cursor to the start of the current line.
        /// </summary>
        /// <returns>True if the cursor moved; false if already at position 0.</returns>
        public bool MoveHome()
        {
            if (_CursorColumn > 0)
            {
                _CursorColumn = 0;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves the cursor to the end of the current line.
        /// </summary>
        /// <returns>True if the cursor moved; false if already at end of line.</returns>
        public bool MoveEnd()
        {
            int endPos = _Lines[_CurrentLineIndex].Length;
            if (_CursorColumn < endPos)
            {
                _CursorColumn = endPos;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds a new line after the current line and moves the cursor to the start of it.
        /// </summary>
        public void InsertNewLine()
        {
            _CurrentLineIndex++;
            _Lines.Insert(_CurrentLineIndex, string.Empty);
            _CursorColumn = 0;
        }

        /// <summary>
        /// Removes the current line and moves back to the end of the previous line.
        /// Used when backspace is pressed at position 0 of a continuation line.
        /// </summary>
        /// <returns>True if the line was removed; false if already on the first line.</returns>
        public bool RemoveCurrentLineAndMergeUp()
        {
            if (_CurrentLineIndex > 0)
            {
                string currentText = _Lines[_CurrentLineIndex];
                _Lines.RemoveAt(_CurrentLineIndex);
                _CurrentLineIndex--;
                _CursorColumn = _Lines[_CurrentLineIndex].Length;
                _Lines[_CurrentLineIndex] += currentText;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears the entire buffer and resets the cursor to position 0, line 0.
        /// </summary>
        public void Clear()
        {
            _Lines.Clear();
            _Lines.Add(string.Empty);
            _CurrentLineIndex = 0;
            _CursorColumn = 0;
        }

        /// <summary>
        /// Replaces the entire buffer contents and places the cursor at the end of the final line.
        /// </summary>
        /// <param name="text">The text to load into the buffer.</param>
        public void SetText(string? text)
        {
            string normalized = text?
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\r", "\n", StringComparison.Ordinal)
                ?? string.Empty;

            string[] lines = normalized.Split('\n');

            _Lines.Clear();
            _Lines.AddRange(lines.Length > 0 ? lines : new[] { string.Empty });

            if (_Lines.Count == 0)
            {
                _Lines.Add(string.Empty);
            }

            _CurrentLineIndex = _Lines.Count - 1;
            _CursorColumn = _Lines[_CurrentLineIndex].Length;
        }

        /// <summary>
        /// Returns the full buffer contents as a single string with newlines between lines.
        /// </summary>
        /// <returns>The complete buffer text.</returns>
        public string GetText()
        {
            return string.Join(Environment.NewLine, _Lines);
        }

        /// <summary>
        /// Gets the text of a specific line.
        /// </summary>
        /// <param name="lineIndex">The zero-based line index.</param>
        /// <returns>The line text, or empty string if out of range.</returns>
        public string GetLine(int lineIndex)
        {
            if (lineIndex >= 0 && lineIndex < _Lines.Count)
            {
                return _Lines[lineIndex];
            }

            return string.Empty;
        }

        #endregion
    }
}
