namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Detects when buffered console input is more likely to be pasted text than
    /// individual interactive key presses.
    /// </summary>
    internal static class InteractivePasteHeuristics
    {
        #region Public-Methods

        /// <summary>
        /// Determines whether an ambiguous trailing Enter should briefly wait for
        /// more buffered characters before being treated as a submit action.
        /// </summary>
        /// <param name="keyBatch">The currently buffered console keys.</param>
        /// <returns>True when the batch could be the start of a pasted multi-line payload.</returns>
        public static bool ShouldWaitForPasteContinuation(IReadOnlyList<ConsoleKeyInfo> keyBatch)
        {
            if (!TryAnalyzeTextBatch(
                keyBatch,
                out int printableCount,
                out int enterCount,
                out _,
                out bool enterBeforeLast,
                out bool endsWithEnter))
            {
                return false;
            }

            return printableCount > 0
                && enterCount == 1
                && !enterBeforeLast
                && endsWithEnter;
        }

        /// <summary>
        /// Determines whether a buffered batch should be inserted as literal pasted
        /// text instead of being interpreted as interactive commands.
        /// </summary>
        /// <param name="keyBatch">The currently buffered console keys.</param>
        /// <param name="continueRecentPaste">Whether a recent batch was already classified as pasted text.</param>
        /// <returns>True when the batch should be inserted directly into the draft buffer.</returns>
        public static bool ShouldTreatBatchAsPastedText(IReadOnlyList<ConsoleKeyInfo> keyBatch, bool continueRecentPaste)
        {
            if (!TryAnalyzeTextBatch(
                keyBatch,
                out int printableCount,
                out int enterCount,
                out int tabCount,
                out bool enterBeforeLast,
                out bool endsWithEnter))
            {
                return false;
            }

            if (enterBeforeLast || enterCount > 1)
            {
                return true;
            }

            if (tabCount > 0 && printableCount > 0)
            {
                return true;
            }

            return continueRecentPaste
                && printableCount > 0
                && enterCount == 1
                && endsWithEnter;
        }

        #endregion

        #region Private-Methods

        private static bool TryAnalyzeTextBatch(
            IReadOnlyList<ConsoleKeyInfo> keyBatch,
            out int printableCount,
            out int enterCount,
            out int tabCount,
            out bool enterBeforeLast,
            out bool endsWithEnter)
        {
            printableCount = 0;
            enterCount = 0;
            tabCount = 0;
            enterBeforeLast = false;
            endsWithEnter = false;

            if (keyBatch == null || keyBatch.Count == 0)
            {
                return false;
            }

            endsWithEnter = keyBatch[keyBatch.Count - 1].Key == ConsoleKey.Enter;

            for (int index = 0; index < keyBatch.Count; index++)
            {
                ConsoleKeyInfo keyInfo = keyBatch[index];

                if ((keyInfo.Modifiers & (ConsoleModifiers.Alt | ConsoleModifiers.Control)) != 0)
                {
                    return false;
                }

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    enterCount++;
                    enterBeforeLast |= index < keyBatch.Count - 1;
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.Tab)
                {
                    tabCount++;
                    continue;
                }

                if (keyInfo.KeyChar != '\0' && !char.IsControl(keyInfo.KeyChar))
                {
                    printableCount++;
                    continue;
                }

                return false;
            }

            return true;
        }

        #endregion
    }
}
