namespace Mux.Core.Conversation
{
    using Mux.Core.Agent;
    using Mux.Core.Telemetry;

    /// <summary>
    /// Accumulates conversation telemetry across turns and produces a <see cref="ConversationStats"/>
    /// snapshot on demand. This is the front-end-agnostic aggregation extracted from the interactive
    /// shell's per-turn bookkeeping: <see cref="RecordTurn"/> folds one completed turn's timing and token
    /// counts into the running session totals, and <see cref="Snapshot"/> renders the current totals into a
    /// carrier the UI reads (filling task counts and cost). The aggregator itself is not thread-safe; the
    /// owner (typically <see cref="ConversationController"/>) serializes access under its own lock.
    /// </summary>
    public sealed class ConversationStatsAggregator
    {
        #region Private-Members

        private int _Turns;
        private long _LastTtftMs = -1;
        private long _LastStreamMs;
        private int _LastContextTokens;
        private long _SessionStreamMs;
        private long _SessionTtftMs;
        private int _TtftSamples;
        private long _InputTokens;
        private long _OutputTokens;
        private long _CachedTokens;

        #endregion

        #region Public-Members

        /// <summary>The number of completed turns recorded this session.</summary>
        public int Turns
        {
            get => _Turns;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Folds one completed turn into the running session totals. Increments the turn count, records the
        /// turn's time-to-first-token and streaming duration, and — when the run reported a completion —
        /// accumulates its input/output token counts and the latest estimated context size. This is the
        /// exact math the interactive shell previously performed inline at turn completion, moved here
        /// verbatim so both front ends aggregate identically.
        /// </summary>
        /// <param name="lastRunCompleted">The run's end-of-run summary, or null when the run produced none
        /// (for example a cancellation or an error before any completion). Token totals are only updated
        /// when this is non-null.</param>
        /// <param name="totalMs">Wall-clock duration of the turn, in milliseconds.</param>
        /// <param name="ttftMs">Time-to-first-token for the turn, in milliseconds, or a negative value when
        /// the turn produced no token (in which case the streaming duration is treated as zero and the
        /// sample does not count toward the average).</param>
        public void RecordTurn(RunCompletedEvent? lastRunCompleted, long totalMs, long ttftMs)
        {
            _Turns++;
            _LastTtftMs = ttftMs;

            long stream = ttftMs >= 0 ? System.Math.Max(0, totalMs - ttftMs) : 0;
            _LastStreamMs = stream;
            _SessionStreamMs += stream;

            if (ttftMs >= 0)
            {
                _SessionTtftMs += ttftMs;
                _TtftSamples++;
            }

            if (lastRunCompleted != null)
            {
                _LastContextTokens = lastRunCompleted.FinalEstimatedTokens;
                _InputTokens += lastRunCompleted.InputTokens;
                _OutputTokens += lastRunCompleted.OutputTokens;
            }
        }

        /// <summary>
        /// Renders the current session totals into a <see cref="ConversationStats"/> carrier. The caller
        /// supplies the focused job's task counts (which are not part of the running totals) and the pricing
        /// context; <see cref="ConversationStats.Busy"/> and <see cref="ConversationStats.Queued"/> are left
        /// at their defaults for the caller to fill from live queue state. Cost is computed from the session
        /// token totals against the pricing table for the active model (0 when unpriced or pricing disabled).
        /// </summary>
        /// <param name="taskTotal">The number of tasks in the focused job's plan (0 when none).</param>
        /// <param name="taskCompleted">The number of completed tasks in the focused job's plan.</param>
        /// <param name="pricing">The pricing table used to estimate session cost. Must not be null.</param>
        /// <param name="model">The active model name used to look up pricing. May be null or empty.</param>
        /// <returns>A populated snapshot; never null.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="pricing"/> is null.</exception>
        public ConversationStats Snapshot(int taskTotal, int taskCompleted, PricingTable pricing, string? model)
        {
            if (pricing is null) throw new System.ArgumentNullException(nameof(pricing));

            ConversationStats stats = new ConversationStats
            {
                Turns = _Turns,
                LastTtftMs = _LastTtftMs,
                LastStreamMs = _LastStreamMs,
                LastContextTokens = _LastContextTokens,
                SessionStreamMs = _SessionStreamMs,
                SessionTtftMs = _SessionTtftMs,
                TtftSamples = _TtftSamples,
                InputTokens = _InputTokens,
                OutputTokens = _OutputTokens,
                CachedTokens = _CachedTokens,
                TaskTotal = taskTotal,
                TaskCompleted = taskCompleted
            };

            stats.SessionCostUsd = pricing.ComputeCostUsd(model ?? string.Empty, stats.InputTokens, stats.CachedTokens, stats.OutputTokens);
            return stats;
        }

        #endregion
    }
}
