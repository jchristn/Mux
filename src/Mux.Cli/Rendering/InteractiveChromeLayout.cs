namespace Mux.Cli.Rendering
{
    using System;

    /// <summary>
    /// Computes prompt-block layout in physical console rows so wrapped input can be
    /// re-rendered in place as the live prompt changes.
    /// </summary>
    public static class InteractiveChromeLayout
    {
        #region Public-Methods

        /// <summary>
        /// Calculates the rendered prompt layout for the current draft buffer.
        /// </summary>
        /// <param name="buffer">The current prompt draft buffer.</param>
        /// <param name="promptWidth">The prompt prefix width in columns.</param>
        /// <param name="bufferWidth">The console buffer width in columns.</param>
        /// <returns>The computed prompt layout.</returns>
        public static PromptLayout Calculate(LineBuffer buffer, int promptWidth, int bufferWidth)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));

            int safeWidth = Math.Max(1, bufferWidth);
            int lineCount = Math.Max(1, buffer.LineCount);
            int[] lineRowOffsets = new int[lineCount];
            int advanceRows = 0;
            int cursorRowOffset = 0;
            int cursorColumn = promptWidth;

            for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
            {
                string text = buffer.GetLine(lineIndex);
                lineRowOffsets[lineIndex] = advanceRows;

                int contentRows = MeasureRenderedRows(promptWidth + text.Length, safeWidth);
                advanceRows += contentRows;

                if (lineIndex == buffer.CurrentLineIndex)
                {
                    (int rowOffset, int column) = GetCellPosition(promptWidth + buffer.CursorColumn, safeWidth);
                    cursorRowOffset = lineRowOffsets[lineIndex] + rowOffset;
                    cursorColumn = column;
                }
            }

            int totalRows = Math.Max(advanceRows, cursorRowOffset + 1);

            return new PromptLayout
            {
                TotalRows = totalRows,
                CursorRowOffset = cursorRowOffset,
                CursorColumn = cursorColumn,
                LineRowOffsets = lineRowOffsets
            };
        }

        #endregion

        #region Private-Methods

        private static int MeasureRenderedRows(int cellCount, int bufferWidth)
        {
            int safeCells = Math.Max(1, cellCount);
            return ((safeCells - 1) / bufferWidth) + 1;
        }

        private static (int RowOffset, int Column) GetCellPosition(int cellOffset, int bufferWidth)
        {
            int safeOffset = Math.Max(0, cellOffset);
            return (safeOffset / bufferWidth, safeOffset % bufferWidth);
        }

        #endregion
    }

    /// <summary>
    /// Physical prompt-block layout derived from the draft buffer and console width.
    /// </summary>
    public class PromptLayout
    {
        /// <summary>
        /// Total physical rows occupied by the prompt block, including any trailing wrapped cursor row.
        /// </summary>
        public int TotalRows { get; set; }

        /// <summary>
        /// The cursor row offset within the prompt block.
        /// </summary>
        public int CursorRowOffset { get; set; }

        /// <summary>
        /// The cursor column within the cursor row.
        /// </summary>
        public int CursorColumn { get; set; }

        /// <summary>
        /// The top-row offset for each logical draft line within the prompt block.
        /// </summary>
        public int[] LineRowOffsets { get; set; } = Array.Empty<int>();
    }
}
