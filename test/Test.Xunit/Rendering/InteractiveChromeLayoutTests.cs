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
    }
}
