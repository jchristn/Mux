namespace Test.Shared.Suites
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Conversation;
    using Mux.Core.Telemetry;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="ConversationStatsAggregator"/> — the front-end-agnostic per-turn
    /// telemetry accumulator promoted from the interactive shell. Cases assert the timing/token math folds
    /// exactly as the shell previously did inline, that a run without a completion leaves token totals
    /// untouched, that a turn without a first token contributes no streaming time and no TTFT sample, and
    /// that <see cref="ConversationStatsAggregator.Snapshot"/> renders totals with task counts and cost.
    /// </summary>
    public static class ConversationStatsSuite
    {
        private const string SuiteId = "ConversationStats";

        /// <summary>
        /// Builds the conversation-stats suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for stats-aggregation cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                SuiteId,
                "Conversation stats aggregation",
                new List<TestCaseDescriptor>
                {
                    Case("SingleTurnFoldsTimingAndTokens", "A completed turn folds timing and token totals", (CancellationToken ct) =>
                    {
                        ConversationStatsAggregator agg = new ConversationStatsAggregator();
                        agg.RecordTurn(Run(finalTokens: 4200, input: 1000, output: 300), totalMs: 900, ttftMs: 200);

                        ConversationStats s = agg.Snapshot(0, 0, new PricingTable(), "model");
                        MuxAssert.AreEqual(1, s.Turns, "one turn recorded");
                        MuxAssert.AreEqual(200L, s.LastTtftMs, "last ttft");
                        MuxAssert.AreEqual(700L, s.LastStreamMs, "stream = total - ttft");
                        MuxAssert.AreEqual(700L, s.SessionStreamMs, "session stream total");
                        MuxAssert.AreEqual(200L, s.SessionTtftMs, "session ttft total");
                        MuxAssert.AreEqual(1, s.TtftSamples, "one ttft sample");
                        MuxAssert.AreEqual(4200, s.LastContextTokens, "context tokens");
                        MuxAssert.AreEqual(1000L, s.InputTokens, "input tokens");
                        MuxAssert.AreEqual(300L, s.OutputTokens, "output tokens");
                        return Task.CompletedTask;
                    }),

                    Case("MultipleTurnsAccumulate", "Session totals accumulate across turns while last-turn fields reflect only the latest", (CancellationToken ct) =>
                    {
                        ConversationStatsAggregator agg = new ConversationStatsAggregator();
                        agg.RecordTurn(Run(1000, 100, 50), totalMs: 500, ttftMs: 100);
                        agg.RecordTurn(Run(2000, 200, 80), totalMs: 800, ttftMs: 300);

                        ConversationStats s = agg.Snapshot(0, 0, new PricingTable(), "model");
                        MuxAssert.AreEqual(2, s.Turns, "two turns");
                        MuxAssert.AreEqual(300L, s.LastTtftMs, "last ttft is latest turn");
                        MuxAssert.AreEqual(500L, s.LastStreamMs, "last stream is latest turn (800-300)");
                        MuxAssert.AreEqual(900L, s.SessionStreamMs, "session stream = 400 + 500");
                        MuxAssert.AreEqual(400L, s.SessionTtftMs, "session ttft = 100 + 300");
                        MuxAssert.AreEqual(2, s.TtftSamples, "two ttft samples");
                        MuxAssert.AreEqual(2000, s.LastContextTokens, "context tokens reflect latest run");
                        MuxAssert.AreEqual(300L, s.InputTokens, "input accumulates");
                        MuxAssert.AreEqual(130L, s.OutputTokens, "output accumulates");
                        return Task.CompletedTask;
                    }),

                    Case("NoRunCompletionLeavesTokensUntouched", "A turn with no completion still counts but leaves token totals at zero", (CancellationToken ct) =>
                    {
                        ConversationStatsAggregator agg = new ConversationStatsAggregator();
                        agg.RecordTurn(null, totalMs: 400, ttftMs: 150);

                        ConversationStats s = agg.Snapshot(0, 0, new PricingTable(), "model");
                        MuxAssert.AreEqual(1, s.Turns, "turn still counted");
                        MuxAssert.AreEqual(250L, s.LastStreamMs, "stream still computed");
                        MuxAssert.AreEqual(0L, s.InputTokens, "no input tokens without completion");
                        MuxAssert.AreEqual(0L, s.OutputTokens, "no output tokens without completion");
                        MuxAssert.AreEqual(0, s.LastContextTokens, "no context tokens without completion");
                        return Task.CompletedTask;
                    }),

                    Case("NoFirstTokenYieldsNoStreamAndNoSample", "A turn without a first token contributes no streaming time and no TTFT sample", (CancellationToken ct) =>
                    {
                        ConversationStatsAggregator agg = new ConversationStatsAggregator();
                        agg.RecordTurn(Run(500, 10, 5), totalMs: 600, ttftMs: -1);

                        ConversationStats s = agg.Snapshot(0, 0, new PricingTable(), "model");
                        MuxAssert.AreEqual(-1L, s.LastTtftMs, "ttft unknown");
                        MuxAssert.AreEqual(0L, s.LastStreamMs, "no stream time when ttft unknown");
                        MuxAssert.AreEqual(0L, s.SessionStreamMs, "no session stream added");
                        MuxAssert.AreEqual(0L, s.SessionTtftMs, "no ttft added");
                        MuxAssert.AreEqual(0, s.TtftSamples, "no ttft sample");
                        return Task.CompletedTask;
                    }),

                    Case("SnapshotCarriesTaskCounts", "Snapshot carries the task counts it is given", (CancellationToken ct) =>
                    {
                        ConversationStatsAggregator agg = new ConversationStatsAggregator();
                        ConversationStats s = agg.Snapshot(5, 2, new PricingTable(), "model");
                        MuxAssert.AreEqual(5, s.TaskTotal, "task total");
                        MuxAssert.AreEqual(2, s.TaskCompleted, "task completed");
                        MuxAssert.IsFalse(s.Busy, "busy defaults false for caller to fill");
                        MuxAssert.AreEqual(0, s.Queued, "queued defaults zero for caller to fill");
                        return Task.CompletedTask;
                    })
                });
        }

        #region Helpers

        private static TestCaseDescriptor Case(string id, string name, System.Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(SuiteId, id, name, body);
        }

        private static RunCompletedEvent Run(int finalTokens, int input, int output)
        {
            return new RunCompletedEvent
            {
                RunId = System.Guid.NewGuid().ToString("N"),
                Status = "completed",
                IterationsCompleted = 1,
                DurationMs = 1,
                FinalEstimatedTokens = finalTokens,
                InputTokens = input,
                OutputTokens = output
            };
        }

        #endregion
    }
}
