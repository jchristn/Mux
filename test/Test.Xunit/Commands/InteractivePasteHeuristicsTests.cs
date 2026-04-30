namespace Test.Xunit.Commands
{
    using System;
    using System.Collections.Generic;
    using global::Xunit;
    using Mux.Cli.Commands;

    /// <summary>
    /// Regression tests for paste-aware interactive console input handling.
    /// </summary>
    public class InteractivePasteHeuristicsTests
    {
        [Fact]
        public void ShouldWaitForPasteContinuation_TrailingEnterAfterTypedText_ReturnsTrue()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                Text('h'),
                Text('i'),
                Enter()
            };

            Assert.True(InteractivePasteHeuristics.ShouldWaitForPasteContinuation(batch));
        }

        [Fact]
        public void ShouldWaitForPasteContinuation_MultiLineBatch_ReturnsFalse()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                Text('a'),
                Enter(),
                Text('b')
            };

            Assert.False(InteractivePasteHeuristics.ShouldWaitForPasteContinuation(batch));
        }

        [Fact]
        public void ShouldTreatBatchAsPastedText_MultiLineBatch_ReturnsTrue()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                Text('a'),
                Text('b'),
                Enter(),
                Text('c'),
                Text('d')
            };

            Assert.True(InteractivePasteHeuristics.ShouldTreatBatchAsPastedText(batch, continueRecentPaste: false));
        }

        [Fact]
        public void ShouldTreatBatchAsPastedText_TrailingEnterWithoutContinuation_ReturnsFalse()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                Text('a'),
                Text('b'),
                Enter()
            };

            Assert.False(InteractivePasteHeuristics.ShouldTreatBatchAsPastedText(batch, continueRecentPaste: false));
        }

        [Fact]
        public void ShouldTreatBatchAsPastedText_TrailingEnterWithContinuation_ReturnsTrue()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                Text('c'),
                Text('d'),
                Enter()
            };

            Assert.True(InteractivePasteHeuristics.ShouldTreatBatchAsPastedText(batch, continueRecentPaste: true));
        }

        [Fact]
        public void ShouldTreatBatchAsPastedText_TabIndentedText_ReturnsTrue()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                Tab(),
                Text('f'),
                Text('o'),
                Text('o')
            };

            Assert.True(InteractivePasteHeuristics.ShouldTreatBatchAsPastedText(batch, continueRecentPaste: false));
        }

        [Fact]
        public void ShouldTreatBatchAsPastedText_ControlModifiedEnter_ReturnsFalse()
        {
            List<ConsoleKeyInfo> batch = new List<ConsoleKeyInfo>
            {
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: true)
            };

            Assert.False(InteractivePasteHeuristics.ShouldTreatBatchAsPastedText(batch, continueRecentPaste: false));
            Assert.False(InteractivePasteHeuristics.ShouldWaitForPasteContinuation(batch));
        }

        private static ConsoleKeyInfo Text(char c)
        {
            return new ConsoleKeyInfo(c, ConsoleKey.A, shift: false, alt: false, control: false);
        }

        private static ConsoleKeyInfo Enter()
        {
            return new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false);
        }

        private static ConsoleKeyInfo Tab()
        {
            return new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false);
        }
    }
}
