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
        /// Determines how many physical line advances are required before prompt
        /// chrome can be rendered after streamed assistant text completes.
        /// </summary>
        /// <param name="assistantTextOpen">Whether assistant text is still logically continuing on the current line.</param>
        /// <param name="outputMayContinue">Whether more output may still arrive for the current response.</param>
        /// <returns>The number of real line advances required before prompt rendering.</returns>
        public static int GetAssistantTextPromptLineAdvanceCount(bool assistantTextOpen, bool outputMayContinue)
        {
            return assistantTextOpen && !outputMayContinue
                ? 2
                : 0;
        }

        /// <summary>
        /// Calculates the first buffer row that is safe for rendering prompt chrome
        /// after the latest output write.
        /// </summary>
        /// <param name="outputCursorTop">The buffer row where the cursor ended after output.</param>
        /// <param name="outputCursorLeft">The buffer column where the cursor ended after output.</param>
        /// <param name="assistantTextOpen">Whether assistant text is still logically continuing on the current line.</param>
        /// <returns>The first row available for chrome rendering.</returns>
        public static int CalculateNextOutputRow(int outputCursorTop, int outputCursorLeft, bool assistantTextOpen)
        {
            return assistantTextOpen || outputCursorLeft > 0
                ? outputCursorTop + 1
                : outputCursorTop;
        }

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

        /// <summary>
        /// Normalizes the width used for prompt rendering when the terminal buffer and
        /// visible window disagree about where line wrapping occurs.
        /// </summary>
        /// <param name="bufferWidth">The reported console buffer width.</param>
        /// <param name="windowWidth">The reported visible console window width.</param>
        /// <returns>The safest width to use for layout and clearing.</returns>
        public static int NormalizeConsoleWidth(int bufferWidth, int windowWidth)
        {
            int safeBufferWidth = Math.Max(0, bufferWidth);
            int safeWindowWidth = Math.Max(0, windowWidth);

            if (safeBufferWidth == 0 && safeWindowWidth == 0)
            {
                return 1;
            }

            if (safeBufferWidth == 0)
            {
                return safeWindowWidth;
            }

            if (safeWindowWidth == 0)
            {
                return safeBufferWidth;
            }

            return Math.Max(1, Math.Min(safeBufferWidth, safeWindowWidth));
        }

        /// <summary>
        /// Calculates the console window top needed to keep the requested row visible.
        /// </summary>
        /// <param name="targetRow">The buffer row that should remain visible.</param>
        /// <param name="currentWindowTop">The current console window top row.</param>
        /// <param name="windowHeight">The visible console window height.</param>
        /// <returns>The preferred window top row.</returns>
        public static int CalculateWindowTopForVisibleRow(int targetRow, int currentWindowTop, int windowHeight)
        {
            int safeTargetRow = Math.Max(0, targetRow);
            int safeWindowTop = Math.Max(0, currentWindowTop);
            int safeWindowHeight = Math.Max(1, windowHeight);
            int visibleBottom = safeWindowTop + safeWindowHeight - 1;

            if (safeTargetRow < safeWindowTop)
            {
                return safeTargetRow;
            }

            if (safeTargetRow > visibleBottom)
            {
                return safeTargetRow - safeWindowHeight + 1;
            }

            return safeWindowTop;
        }

        /// <summary>
        /// Calculates the full prompt-chrome row range that must be cleared before a redraw.
        /// </summary>
        /// <param name="previousTop">The top row used by the previous prompt render.</param>
        /// <param name="previousRowCount">The number of rows used by the previous prompt render.</param>
        /// <param name="nextTop">The top row needed for the next prompt render.</param>
        /// <param name="nextRowCount">The number of rows needed for the next prompt render.</param>
        /// <returns>The top row and total row count to clear.</returns>
        public static (int Top, int RowCount) CalculateClearRegion(
            int previousTop,
            int previousRowCount,
            int nextTop,
            int nextRowCount)
        {
            int safePreviousRowCount = Math.Max(0, previousRowCount);
            int safeNextRowCount = Math.Max(0, nextRowCount);
            int safePreviousTop = Math.Max(0, previousTop);
            int safeNextTop = Math.Max(0, nextTop);

            if (safePreviousRowCount == 0)
            {
                return (safeNextTop, safeNextRowCount);
            }

            if (safeNextRowCount == 0)
            {
                return (safePreviousTop, safePreviousRowCount);
            }

            int clearTop = Math.Min(safePreviousTop, safeNextTop);
            int previousBottom = safePreviousTop + safePreviousRowCount - 1;
            int nextBottom = safeNextTop + safeNextRowCount - 1;
            int clearBottom = Math.Max(previousBottom, nextBottom);

            return (clearTop, clearBottom - clearTop + 1);
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
