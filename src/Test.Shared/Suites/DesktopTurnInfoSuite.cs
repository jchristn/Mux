namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.Conversation;
    using Mux.Core.Llm;
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
                        MuxAssert.AreEqual("ⓘ  1.5 s · 345 tokens processed · 4 model calls", Sample().BuildSummary(), "seconds summary");
                        MuxAssert.AreEqual("ⓘ  450 ms · 0 tokens processed", new TurnInfo { TotalMs = 450 }.BuildSummary(), "sub-second summary");
                        MuxAssert.AreEqual("ⓘ  0 ms · 357,549 tokens processed · 1 model call", new TurnInfo { TotalTokens = 357549, ModelCalls = 1 }.BuildSummary(), "grouped count and singular call");
                        MuxAssert.Contains("0 model calls", new TurnInfo { ModelCalls = 0 }.BuildSummary(), "known zero is distinct from unknown");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "DetailsIncludeTimingAndActivity", "Details carry timing, tokens, steps, tool calls, errors, context, and status", (CancellationToken ct) =>
                    {
                        string details = Sample().BuildDetails(null, null);
                        MuxAssert.Contains("First model call: time to first token 200 ms", details, "ttft");
                        MuxAssert.Contains("Model streaming (all calls): 300 ms", details, "streaming");
                        MuxAssert.Contains("Total turn time: 1.5 s", details, "total");
                        MuxAssert.Contains("input 300 · output 45 · total 345", details, "tokens");
                        MuxAssert.Contains("Model calls: 4 · steps: 3 · tool calls: 2 · run errors: 1", details, "activity");
                        MuxAssert.Contains("Estimated context: ~4096 tokens · status: completed", details, "context and status");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MultipleCallsUseRecordedTimings", "Tool/thinking turns use first-call TTFT and the sum of all model streams", (CancellationToken ct) =>
                    {
                        LlmRunTiming timing = new LlmRunTiming();
                        timing.Add(new LlmCallMetrics { TimeToFirstTokenMs = 773, StreamingMs = 1246 });
                        timing.Add(new LlmCallMetrics { TimeToFirstTokenMs = 793, StreamingMs = 114 });
                        timing.Add(new LlmCallMetrics { TimeToFirstTokenMs = 807, StreamingMs = 0 });
                        timing.Add(new LlmCallMetrics { TimeToFirstTokenMs = 722, StreamingMs = 53 });
                        MuxAssert.AreEqual(773L, timing.FirstCallTtftMs, "first call, not last answer");
                        MuxAssert.AreEqual(1413L, timing.StreamingMs, "all streams, including zero-duration stream");
                        TurnInfo info = new TurnInfo { TotalMs = 10121, TtftMs = timing.FirstCallTtftMs, StreamingMs = timing.StreamingMs, ModelCalls = 4 };
                        string details = info.BuildDetails(null, null);
                        MuxAssert.Contains("first token 773 ms", details, "first-call timing displayed");
                        MuxAssert.Contains("(all calls): 1413 ms", details, "real aggregate displayed");
                        MuxAssert.Contains("Total turn time: 10.1 s", details, "turn duration kept separate");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "IncompleteTimingRemainsUnknown", "Missing call timings do not understate aggregate streaming or substitute a later TTFT", (CancellationToken ct) =>
                    {
                        LlmRunTiming timing = new LlmRunTiming();
                        MuxAssert.IsNull(timing.FirstCallTtftMs, "no first call");
                        MuxAssert.IsNull(timing.StreamingMs, "no stream");
                        timing.Add(null);
                        timing.Add(new LlmCallMetrics { TimeToFirstTokenMs = 800, StreamingMs = 50 });
                        MuxAssert.IsNull(timing.FirstCallTtftMs, "later TTFT does not replace missing first call");
                        MuxAssert.IsNull(timing.StreamingMs, "partial stream sum not presented as complete");
                        LlmRunTiming partial = new LlmRunTiming();
                        partial.Add(new LlmCallMetrics { TimeToFirstTokenMs = 0, StreamingMs = 0 });
                        MuxAssert.AreEqual(0L, partial.FirstCallTtftMs, "reported zero is valid");
                        MuxAssert.AreEqual(0L, partial.StreamingMs, "reported zero stream is valid");
                        partial.Add(new LlmCallMetrics());
                        MuxAssert.AreEqual(0L, partial.FirstCallTtftMs, "first-call metric preserved");
                        MuxAssert.IsNull(partial.StreamingMs, "one missing stream makes sum unknown");
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
                            MuxAssert.Contains("{5}", activity, locale.Code + " activity format has every placeholder");
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
                        MuxAssert.Contains("First model call: time to first token 200 ms", details, "timing fallback");
                        MuxAssert.Contains("steps: 3", details, "activity fallback");
                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor(SuiteId, "MissingTimingIsNotFabricated", "Missing timings stay unknown rather than being derived from total turn time", (CancellationToken ct) =>
                    {
                        TurnInfo missing = new TurnInfo { TotalMs = 10121, ModelCalls = 4 };
                        MuxAssert.IsNull(missing.StreamingMs, "missing streaming stays unknown");
                        MuxAssert.Contains("first token — ms", missing.BuildDetails(null, null), "no false zero TTFT");
                        MuxAssert.Contains("(all calls): — ms", missing.BuildDetails(null, null), "turn duration is not streaming");
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
                StreamingMs = 300,
                InputTokens = 300,
                OutputTokens = 45,
                TotalTokens = 345,
                Steps = 3,
                ModelCalls = 4,
                ToolCalls = 2,
                Errors = 1,
                ContextTokens = 4096,
                Status = "completed"
            };
        }

        #endregion
    }
}
