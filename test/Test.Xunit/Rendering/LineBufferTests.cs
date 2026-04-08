namespace Test.Xunit.Rendering
{
    using global::Xunit;
    using Mux.Cli.Rendering;

    /// <summary>
    /// Unit tests for the <see cref="LineBuffer"/> class covering insert, delete,
    /// cursor movement (left, right, home, end), and multi-line operations.
    /// </summary>
    public class LineBufferTests
    {
        #region Insert

        /// <summary>
        /// Inserting characters at the end appends them in order.
        /// </summary>
        [Fact]
        public void Insert_AtEnd_AppendsCharacters()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.Insert('c');

            Assert.Equal("abc", buffer.GetText());
            Assert.Equal(3, buffer.CursorColumn);
        }

        /// <summary>
        /// Inserting a character mid-line shifts existing text right.
        /// </summary>
        [Fact]
        public void Insert_MidLine_ShiftsTextRight()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('c');
            buffer.MoveLeft();
            buffer.Insert('b');

            Assert.Equal("abc", buffer.GetText());
            Assert.Equal(2, buffer.CursorColumn);
        }

        /// <summary>
        /// Inserting at position 0 prepends the character.
        /// </summary>
        [Fact]
        public void Insert_AtStart_Prepends()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('b');
            buffer.MoveHome();
            buffer.Insert('a');

            Assert.Equal("ab", buffer.GetText());
            Assert.Equal(1, buffer.CursorColumn);
        }

        #endregion

        #region Backspace

        /// <summary>
        /// Backspace at the end removes the last character.
        /// </summary>
        [Fact]
        public void Backspace_AtEnd_RemovesLastChar()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.Insert('c');

            bool result = buffer.Backspace();

            Assert.True(result);
            Assert.Equal("ab", buffer.GetText());
            Assert.Equal(2, buffer.CursorColumn);
        }

        /// <summary>
        /// Backspace mid-line removes the character before the cursor.
        /// </summary>
        [Fact]
        public void Backspace_MidLine_RemovesCharBeforeCursor()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.Insert('c');
            buffer.MoveLeft(); // cursor at 2 (before 'c')
            buffer.Backspace(); // removes 'b'

            Assert.Equal("ac", buffer.GetText());
            Assert.Equal(1, buffer.CursorColumn);
        }

        /// <summary>
        /// Backspace at position 0 returns false and does nothing.
        /// </summary>
        [Fact]
        public void Backspace_AtStart_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.MoveHome();

            bool result = buffer.Backspace();

            Assert.False(result);
            Assert.Equal("a", buffer.GetText());
            Assert.Equal(0, buffer.CursorColumn);
        }

        /// <summary>
        /// Backspace on an empty buffer returns false.
        /// </summary>
        [Fact]
        public void Backspace_EmptyBuffer_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();

            bool result = buffer.Backspace();

            Assert.False(result);
            Assert.Equal("", buffer.GetText());
        }

        #endregion

        #region Delete

        /// <summary>
        /// Delete at position 0 removes the first character.
        /// </summary>
        [Fact]
        public void Delete_AtStart_RemovesFirstChar()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.MoveHome();

            bool result = buffer.Delete();

            Assert.True(result);
            Assert.Equal("b", buffer.GetText());
            Assert.Equal(0, buffer.CursorColumn);
        }

        /// <summary>
        /// Delete at end of line returns false.
        /// </summary>
        [Fact]
        public void Delete_AtEnd_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');

            bool result = buffer.Delete();

            Assert.False(result);
            Assert.Equal("a", buffer.GetText());
        }

        #endregion

        #region MoveLeft-MoveRight

        /// <summary>
        /// MoveLeft decrements the cursor column.
        /// </summary>
        [Fact]
        public void MoveLeft_FromEnd_DecrementsCursor()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');

            bool result = buffer.MoveLeft();

            Assert.True(result);
            Assert.Equal(1, buffer.CursorColumn);
        }

        /// <summary>
        /// MoveLeft at position 0 returns false.
        /// </summary>
        [Fact]
        public void MoveLeft_AtStart_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();

            bool result = buffer.MoveLeft();

            Assert.False(result);
            Assert.Equal(0, buffer.CursorColumn);
        }

        /// <summary>
        /// MoveRight increments the cursor column.
        /// </summary>
        [Fact]
        public void MoveRight_FromStart_IncrementsCursor()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.MoveHome();

            bool result = buffer.MoveRight();

            Assert.True(result);
            Assert.Equal(1, buffer.CursorColumn);
        }

        /// <summary>
        /// MoveRight at end of line returns false.
        /// </summary>
        [Fact]
        public void MoveRight_AtEnd_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');

            bool result = buffer.MoveRight();

            Assert.False(result);
            Assert.Equal(1, buffer.CursorColumn);
        }

        #endregion

        #region Home-End

        /// <summary>
        /// MoveHome sets cursor to position 0.
        /// </summary>
        [Fact]
        public void MoveHome_SetsCursorToZero()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.Insert('c');

            bool result = buffer.MoveHome();

            Assert.True(result);
            Assert.Equal(0, buffer.CursorColumn);
        }

        /// <summary>
        /// MoveHome when already at 0 returns false.
        /// </summary>
        [Fact]
        public void MoveHome_AlreadyAtStart_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();

            bool result = buffer.MoveHome();

            Assert.False(result);
        }

        /// <summary>
        /// MoveEnd sets cursor to end of line.
        /// </summary>
        [Fact]
        public void MoveEnd_SetsCursorToEndOfLine()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.MoveHome();

            bool result = buffer.MoveEnd();

            Assert.True(result);
            Assert.Equal(2, buffer.CursorColumn);
        }

        /// <summary>
        /// MoveEnd when already at end returns false.
        /// </summary>
        [Fact]
        public void MoveEnd_AlreadyAtEnd_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');

            bool result = buffer.MoveEnd();

            Assert.False(result);
        }

        #endregion

        #region MultiLine

        /// <summary>
        /// InsertNewLine adds a second line and moves the cursor to it.
        /// </summary>
        [Fact]
        public void InsertNewLine_AddsLineAndMovesCursor()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.InsertNewLine();

            Assert.Equal(2, buffer.LineCount);
            Assert.Equal(1, buffer.CurrentLineIndex);
            Assert.Equal(0, buffer.CursorColumn);
            Assert.Equal("a" + System.Environment.NewLine, buffer.GetText());
        }

        /// <summary>
        /// Inserting text on a new line works independently of the first line.
        /// </summary>
        [Fact]
        public void InsertNewLine_ThenType_SecondLineHasContent()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.InsertNewLine();
            buffer.Insert('b');

            Assert.Equal("a" + System.Environment.NewLine + "b", buffer.GetText());
            Assert.Equal("b", buffer.CurrentLine);
        }

        /// <summary>
        /// RemoveCurrentLineAndMergeUp merges the current line text into the previous line.
        /// </summary>
        [Fact]
        public void RemoveCurrentLineAndMergeUp_MergesText()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.InsertNewLine();
            buffer.Insert('b');

            bool result = buffer.RemoveCurrentLineAndMergeUp();

            Assert.True(result);
            Assert.Equal(1, buffer.LineCount);
            Assert.Equal("ab", buffer.GetText());
            Assert.Equal(1, buffer.CursorColumn); // cursor at join point
        }

        /// <summary>
        /// RemoveCurrentLineAndMergeUp on the first line returns false.
        /// </summary>
        [Fact]
        public void RemoveCurrentLineAndMergeUp_OnFirstLine_ReturnsFalse()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');

            bool result = buffer.RemoveCurrentLineAndMergeUp();

            Assert.False(result);
        }

        #endregion

        #region Clear

        /// <summary>
        /// Clear resets everything to empty state.
        /// </summary>
        [Fact]
        public void Clear_ResetsToEmpty()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.Insert('a');
            buffer.Insert('b');
            buffer.InsertNewLine();
            buffer.Insert('c');

            buffer.Clear();

            Assert.Equal("", buffer.GetText());
            Assert.Equal(0, buffer.CurrentLineIndex);
            Assert.Equal(0, buffer.CursorColumn);
            Assert.Equal(1, buffer.LineCount);
        }

        /// <summary>
        /// SetText replaces the buffer with a single line and moves the cursor to the end.
        /// </summary>
        [Fact]
        public void SetText_SingleLine_LoadsContentAndMovesCursorToEnd()
        {
            LineBuffer buffer = new LineBuffer();

            buffer.SetText("prompt");

            Assert.Equal("prompt", buffer.GetText());
            Assert.Equal(0, buffer.CurrentLineIndex);
            Assert.Equal(6, buffer.CursorColumn);
            Assert.Equal(1, buffer.LineCount);
        }

        /// <summary>
        /// SetText normalizes multi-line content and positions the cursor on the final line.
        /// </summary>
        [Fact]
        public void SetText_MultiLine_LoadsContentAndMovesCursorToFinalLine()
        {
            LineBuffer buffer = new LineBuffer();

            buffer.SetText("first\r\nsecond");

            Assert.Equal("first" + System.Environment.NewLine + "second", buffer.GetText());
            Assert.Equal(1, buffer.CurrentLineIndex);
            Assert.Equal(6, buffer.CursorColumn);
            Assert.Equal(2, buffer.LineCount);
        }

        #endregion

        #region ComplexSequences

        /// <summary>
        /// Simulates typing "hello", moving left 3 positions, inserting "XY",
        /// verifying the result is "heXYllo".
        /// </summary>
        [Fact]
        public void ComplexInsert_MidWord()
        {
            LineBuffer buffer = new LineBuffer();
            foreach (char c in "hello")
            {
                buffer.Insert(c);
            }

            buffer.MoveLeft();
            buffer.MoveLeft();
            buffer.MoveLeft();
            buffer.Insert('X');
            buffer.Insert('Y');

            Assert.Equal("heXYllo", buffer.GetText());
            Assert.Equal(4, buffer.CursorColumn);
        }

        /// <summary>
        /// Simulates Home, type, End, type to verify full navigation cycle.
        /// </summary>
        [Fact]
        public void HomeEndCycle_InsertsAtBothEnds()
        {
            LineBuffer buffer = new LineBuffer();
            foreach (char c in "middle")
            {
                buffer.Insert(c);
            }

            buffer.MoveHome();
            buffer.Insert('[');
            buffer.MoveEnd();
            buffer.Insert(']');

            Assert.Equal("[middle]", buffer.GetText());
        }

        #endregion
    }
}
