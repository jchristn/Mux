namespace Test.Xunit.Rendering
{
    using global::Xunit;
    using Mux.Cli.Rendering;

    /// <summary>
    /// Unit tests for prompt chrome layout calculations.
    /// </summary>
    public class InteractiveChromeLayoutTests
    {
        [Fact]
        public void Calculate_ShortSingleLine_UsesSingleRow()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.SetText("hello");

            PromptLayout layout = InteractiveChromeLayout.Calculate(buffer, promptWidth: 5, bufferWidth: 80);

            Assert.Equal(1, layout.TotalRows);
            Assert.Equal(0, layout.CursorRowOffset);
            Assert.Equal(10, layout.CursorColumn);
            Assert.Equal(new[] { 0 }, layout.LineRowOffsets);
        }

        [Fact]
        public void Calculate_ExactFitSingleLine_ReservesWrappedCursorRow()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.SetText("hello");

            PromptLayout layout = InteractiveChromeLayout.Calculate(buffer, promptWidth: 5, bufferWidth: 10);

            Assert.Equal(2, layout.TotalRows);
            Assert.Equal(1, layout.CursorRowOffset);
            Assert.Equal(0, layout.CursorColumn);
            Assert.Equal(new[] { 0 }, layout.LineRowOffsets);
        }

        [Fact]
        public void Calculate_MultiLinePrompt_TracksPhysicalOffsets()
        {
            LineBuffer buffer = new LineBuffer();
            buffer.SetText("abcde" + System.Environment.NewLine + "xy");

            PromptLayout layout = InteractiveChromeLayout.Calculate(buffer, promptWidth: 5, bufferWidth: 10);

            Assert.Equal(new[] { 0, 1 }, layout.LineRowOffsets);
            Assert.Equal(1, layout.CursorRowOffset);
            Assert.Equal(7, layout.CursorColumn);
            Assert.Equal(2, layout.TotalRows);
        }

        [Fact]
        public void CalculateNextOutputRow_OpenAssistantTextMidLine_AdvancesToNextRow()
        {
            int nextRow = InteractiveChromeLayout.CalculateNextOutputRow(
                outputCursorTop: 12,
                outputCursorLeft: 9,
                assistantTextOpen: true);

            Assert.Equal(13, nextRow);
        }

        [Fact]
        public void CalculateNextOutputRow_OpenAssistantTextAtColumnZero_AdvancesToNextRow()
        {
            int nextRow = InteractiveChromeLayout.CalculateNextOutputRow(
                outputCursorTop: 12,
                outputCursorLeft: 0,
                assistantTextOpen: true);

            Assert.Equal(13, nextRow);
        }

        [Fact]
        public void CalculateNextOutputRow_ClosedAssistantTextMidLine_AdvancesToNextRow()
        {
            int nextRow = InteractiveChromeLayout.CalculateNextOutputRow(
                outputCursorTop: 12,
                outputCursorLeft: 5,
                assistantTextOpen: false);

            Assert.Equal(13, nextRow);
        }

        [Fact]
        public void CalculateNextOutputRow_ClosedAssistantTextAtColumnZero_StaysOnCurrentRow()
        {
            int nextRow = InteractiveChromeLayout.CalculateNextOutputRow(
                outputCursorTop: 12,
                outputCursorLeft: 0,
                assistantTextOpen: false);

            Assert.Equal(12, nextRow);
        }

        [Fact]
        public void GetAssistantTextPromptLineAdvanceCount_CompletedStreaming_ReturnsTwo()
        {
            int lineAdvanceCount = InteractiveChromeLayout.GetAssistantTextPromptLineAdvanceCount(
                assistantTextOpen: true,
                outputMayContinue: false);

            Assert.Equal(2, lineAdvanceCount);
        }

        [Fact]
        public void GetAssistantTextPromptLineAdvanceCount_ActiveStreaming_ReturnsZero()
        {
            int lineAdvanceCount = InteractiveChromeLayout.GetAssistantTextPromptLineAdvanceCount(
                assistantTextOpen: true,
                outputMayContinue: true);

            Assert.Equal(0, lineAdvanceCount);
        }

        [Fact]
        public void GetAssistantTextPromptLineAdvanceCount_NoOpenAssistantText_ReturnsZero()
        {
            int lineAdvanceCount = InteractiveChromeLayout.GetAssistantTextPromptLineAdvanceCount(
                assistantTextOpen: false,
                outputMayContinue: false);

            Assert.Equal(0, lineAdvanceCount);
        }

        [Fact]
        public void CalculateWindowTopForVisibleRow_TargetAlreadyVisible_KeepsWindowTop()
        {
            int windowTop = InteractiveChromeLayout.CalculateWindowTopForVisibleRow(
                targetRow: 15,
                currentWindowTop: 10,
                windowHeight: 10);

            Assert.Equal(10, windowTop);
        }

        [Fact]
        public void CalculateWindowTopForVisibleRow_TargetBelowVisibleWindow_ScrollsDown()
        {
            int windowTop = InteractiveChromeLayout.CalculateWindowTopForVisibleRow(
                targetRow: 25,
                currentWindowTop: 10,
                windowHeight: 10);

            Assert.Equal(16, windowTop);
        }

        [Fact]
        public void CalculateWindowTopForVisibleRow_TargetAboveVisibleWindow_ScrollsUp()
        {
            int windowTop = InteractiveChromeLayout.CalculateWindowTopForVisibleRow(
                targetRow: 4,
                currentWindowTop: 10,
                windowHeight: 10);

            Assert.Equal(4, windowTop);
        }

        [Fact]
        public void NormalizeConsoleWidth_WhenBufferAndWindowDiffer_UsesVisibleWidth()
        {
            int width = InteractiveChromeLayout.NormalizeConsoleWidth(
                bufferWidth: 240,
                windowWidth: 80);

            Assert.Equal(80, width);
        }

        [Fact]
        public void NormalizeConsoleWidth_WhenWindowWidthUnavailable_UsesBufferWidth()
        {
            int width = InteractiveChromeLayout.NormalizeConsoleWidth(
                bufferWidth: 120,
                windowWidth: 0);

            Assert.Equal(120, width);
        }

        [Fact]
        public void CalculateClearRegion_WhenPromptGrows_ClearsOldAndNewRows()
        {
            (int top, int rowCount) = InteractiveChromeLayout.CalculateClearRegion(
                previousTop: 20,
                previousRowCount: 1,
                nextTop: 20,
                nextRowCount: 2);

            Assert.Equal(20, top);
            Assert.Equal(2, rowCount);
        }

        [Fact]
        public void CalculateClearRegion_WhenPromptMovesUp_ClearsFullUnion()
        {
            (int top, int rowCount) = InteractiveChromeLayout.CalculateClearRegion(
                previousTop: 30,
                previousRowCount: 2,
                nextTop: 29,
                nextRowCount: 2);

            Assert.Equal(29, top);
            Assert.Equal(3, rowCount);
        }
    }
}
