namespace Test.Xunit.Commands
{
    using global::Xunit;
    using Mux.Cli.Commands;

    /// <summary>
    /// Unit tests for <see cref="SessionTitleHelper"/>.
    /// </summary>
    public class SessionTitleHelperTests
    {
        /// <summary>
        /// Verifies that normalization strips boilerplate prefixes, quotes, multiline output, and trailing punctuation.
        /// </summary>
        [Fact]
        public void Normalize_StripsFormattingNoise()
        {
            string normalized = SessionTitleHelper.Normalize("Title: \"Refactor auth flow.\"\nextra line", "Fallback");

            Assert.Equal("Refactor auth flow", normalized);
        }

        /// <summary>
        /// Verifies that normalization falls back when the title is empty after trimming.
        /// </summary>
        [Fact]
        public void Normalize_UsesFallbackWhenEmpty()
        {
            string normalized = SessionTitleHelper.Normalize("  ", "Current title");

            Assert.Equal("Current title", normalized);
        }
    }
}
