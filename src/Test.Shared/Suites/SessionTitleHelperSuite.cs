namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Commands;
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
                    })
                });
        }
    }
}
