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
    }
}
