namespace Test.Xunit.Rendering
{
    using global::Xunit;
    using Mux.Cli.Rendering;

    /// <summary>
    /// Unit tests for session prompt history navigation in interactive mode.
    /// </summary>
    public class PromptHistoryTests
    {
        /// <summary>
        /// History navigation should return false when no prompts have been submitted.
        /// </summary>
        [Fact]
        public void TryMovePrevious_WithoutEntries_ReturnsFalse()
        {
            PromptHistory history = new PromptHistory();

            bool result = history.TryMovePrevious(string.Empty, out string prompt);

            Assert.False(result);
            Assert.Equal(string.Empty, prompt);
        }

        /// <summary>
        /// Navigating back to history and then forward again should restore the in-progress draft.
        /// </summary>
        [Fact]
        public void TryMovePrevious_ThenTryMoveNext_RestoresDraft()
        {
            PromptHistory history = new PromptHistory();
            history.Add("first");
            history.Add("second");

            bool movedPrevious = history.TryMovePrevious("draft", out string previousPrompt);
            bool movedNext = history.TryMoveNext(out string restoredPrompt);

            Assert.True(movedPrevious);
            Assert.Equal("second", previousPrompt);
            Assert.True(movedNext);
            Assert.Equal("draft", restoredPrompt);
            Assert.False(history.IsBrowsing);
        }

        /// <summary>
        /// Repeated previous navigation should walk backward from the newest prompt to the oldest.
        /// </summary>
        [Fact]
        public void TryMovePrevious_MultipleTimes_WalksBackwardThroughHistory()
        {
            PromptHistory history = new PromptHistory();
            history.Add("first");
            history.Add("second");
            history.Add("third");

            history.TryMovePrevious(string.Empty, out string firstRecall);
            history.TryMovePrevious(string.Empty, out string secondRecall);
            history.TryMovePrevious(string.Empty, out string thirdRecall);

            Assert.Equal("third", firstRecall);
            Assert.Equal("second", secondRecall);
            Assert.Equal("first", thirdRecall);
        }
    }
}
