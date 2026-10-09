namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.Conversation;
    using Mux.Desktop.I18n;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="TurnInfo"/>, the telemetry behind the desktop chat turn's info button: the
    /// summary label, the localized details, streaming math, and the fallbacks for missing or malformed formats.
    /// </summary>
    public static class DesktopTurnInfoSuite
    {
        #region Private-Members

        private const string SuiteId = "DesktopTurnInfo";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the turn-info suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the turn-info cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Desktop chat turn info",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "SummaryShowsTotalAndTokens", "The summary label shows the total time and total tokens", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual("ⓘ  1.5 s · 345 tokens", Sample().BuildSummary(), "seconds summary");
                        MuxAssert.AreEqual("ⓘ  450 ms · 0 tokens", new TurnInfo { TotalMs = 450 }.BuildSummary(), "sub-second summary");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DetailsIncludeTimingAndActivity", "Details carry timing, tokens, steps, tool calls, errors, context, and status", (CancellationToken ct) =>
                    {
                        string details = Sample().BuildDetails(null, null);
                        MuxAssert.Contains("Time to first token: 200 ms", details, "ttft");
                        MuxAssert.Contains("Streaming: 1300 ms", details, "streaming");
                        MuxAssert.Contains("Total: 1.5 s", details, "total");
                        MuxAssert.Contains("input 300 · output 45 · total 345", details, "tokens");
                        MuxAssert.Contains("Steps: 3 · tool calls: 2 · errors: 1", details, "activity");
                        MuxAssert.Contains("Context: ~4096 tokens · status: completed", details, "context and status");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DetailsUseLocalizedFormats", "Every locale's turn-info formats render without falling back", (CancellationToken ct) =>
                    {
                        LocalizationService service = new LocalizationService();
                        int locales = 0;
                        foreach (LocaleInfo locale in service.SupportedLocales)
                        {
                            locales++;
                            service.SetActiveLocale(locale.Code);
                            string timing = service.Get("main.turnInfo.tip");
                            string activity = service.Get("main.turnInfo.more");
                            MuxAssert.IsFalse(string.IsNullOrWhiteSpace(activity) || activity == "main.turnInfo.more", locale.Code + " has main.turnInfo.more");
                            MuxAssert.Contains("{4}", activity, locale.Code + " activity format has every placeholder");
                            string details = Sample().BuildDetails(timing, activity);
                            MuxAssert.Contains("4096", details, locale.Code + " shows the context size");
                            MuxAssert.Contains("345", details, locale.Code + " shows the total tokens");
                            MuxAssert.Contains("completed", details, locale.Code + " shows the status");
                        }

                        MuxAssert.AreEqual(11, locales, "eleven locales checked");

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MalformedFormatFallsBack", "A malformed or blank localized format falls back to English instead of throwing", (CancellationToken ct) =>
                    {
                        string details = Sample().BuildDetails("broken {9", "   ");
                        MuxAssert.Contains("Time to first token: 200 ms", details, "timing fallback");
                        MuxAssert.Contains("Steps: 3", details, "activity fallback");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "StreamingNeverNegative", "Streaming time is never negative and treats a missing first token as zero", (CancellationToken ct) =>
                    {
                        MuxAssert.AreEqual(0L, new TurnInfo { TotalMs = 100, TtftMs = 500 }.StreamingMs, "ttft after total");
                        MuxAssert.AreEqual(800L, new TurnInfo { TotalMs = 800, TtftMs = null }.StreamingMs, "no first token");
                        MuxAssert.Contains("status: unknown", new TurnInfo().BuildDetails(null, null), "blank status");
                        MuxAssert.AreEqual("0 ms", TurnInfo.FormatMs(-5), "negative duration clamps");
                        return Task.CompletedTask;
                    })
                });
        }

        #endregion

        #region Private-Methods

        private static TurnInfo Sample()
        {
            return new TurnInfo
            {
                TotalMs = 1500,
                TtftMs = 200,
                InputTokens = 300,
                OutputTokens = 45,
                TotalTokens = 345,
                Steps = 3,
                ToolCalls = 2,
                Errors = 1,
                ContextTokens = 4096,
                Status = "completed"
            };
        }

        #endregion
    }
}
