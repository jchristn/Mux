namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="SessionTitleHelper"/> normalization. Ported from the
    /// <c>SessionTitleHelperTests</c> xUnit suite.
    /// </summary>
    public static class SessionTitleHelperSuite
    {
        /// <summary>
        /// Builds the session-title-helper suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the session-title-helper cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "SessionTitleHelper",
                "Session title normalization",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("SessionTitleHelper", "NormalizeStripsFormattingNoise", "Normalization strips boilerplate, quotes, extra lines, and trailing punctuation", (CancellationToken ct) =>
                    {
                        string normalized = SessionTitleHelper.Normalize("Title: \"Refactor auth flow.\"\nextra line", "Fallback");
                        MuxAssert.AreEqual("Refactor auth flow", normalized, "normalized title");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("SessionTitleHelper", "NormalizeUsesFallbackWhenEmpty", "Normalization falls back when the title is empty after trimming", (CancellationToken ct) =>
                    {
                        string normalized = SessionTitleHelper.Normalize("  ", "Current title");
                        MuxAssert.AreEqual("Current title", normalized, "fallback title");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("SessionTitleHelper", "NormalizeKeepsTrailingContentQuote", "A quote that is part of the title (say \"web1\") keeps its closing quote", (CancellationToken ct) =>
                    {
                        // The title is not wrapped in quotes — the closing quote belongs to the content and must
                        // not be stripped (the "last character truncated" bug).
                        string normalized = SessionTitleHelper.Normalize("say \"web1\"", "Fallback");
                        MuxAssert.AreEqual("say \"web1\"", normalized, "closing content quote preserved");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("SessionTitleHelper", "NormalizeUnwrapsMatchingQuotePair", "A title fully wrapped in a matching quote pair is unwrapped", (CancellationToken ct) =>
                    {
                        string normalized = SessionTitleHelper.Normalize("\"say web1\"", "Fallback");
                        MuxAssert.AreEqual("say web1", normalized, "wrapping quotes removed");
                        return Task.CompletedTask;
                    })
                });
        }
    }
}
