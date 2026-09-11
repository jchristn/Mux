namespace Mux.Core.Telemetry
{
    /// <summary>
    /// A raw aggregate row produced by grouping usage events by (bucket, model, adapter, endpoint, command,
    /// call-kind). The query service rolls these up into summaries, time series, and breakdowns, and derives
    /// cost from the token sums using the pricing table. Timing sums carry their own sample counts so
    /// averages can be recomputed correctly when rows are combined.
    /// </summary>
    public sealed class UsageAggregateRow
    {
        #region Public-Members

        /// <summary>The bucket start (Unix epoch ms), or 0 when the query is not time-bucketed.</summary>
        public long BucketStartUnixMs { get; set; }

        /// <summary>The model identifier for this group.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>The adapter/provider family for this group.</summary>
        public string AdapterType { get; set; } = string.Empty;

        /// <summary>The endpoint name for this group.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>The command/mode for this group, or empty.</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>The call kind for this group.</summary>
        public string CallKind { get; set; } = string.Empty;

        /// <summary>The number of calls in the group.</summary>
        public long Calls { get; set; }

        /// <summary>The number of failed calls in the group.</summary>
        public long Errors { get; set; }

        /// <summary>Summed input tokens.</summary>
        public long InputTokens { get; set; }

        /// <summary>Summed cached tokens.</summary>
        public long CachedTokens { get; set; }

        /// <summary>Summed output tokens.</summary>
        public long OutputTokens { get; set; }

        /// <summary>Summed total tokens.</summary>
        public long TotalTokens { get; set; }

        /// <summary>Summed time-to-first-token (ms) over rows that reported one.</summary>
        public long TtftSumMs { get; set; }

        /// <summary>The number of rows that reported a time-to-first-token.</summary>
        public long TtftCount { get; set; }

        /// <summary>Summed total runtime (ms) over rows that reported one.</summary>
        public long TotalMsSum { get; set; }

        /// <summary>The number of rows that reported a total runtime.</summary>
        public long TotalMsCount { get; set; }

        /// <summary>Summed streaming duration (ms).</summary>
        public long StreamMsSum { get; set; }

        /// <summary>The number of rows that reported a streaming duration.</summary>
        public long StreamMsCount { get; set; }

        /// <summary>Summed tokens-per-second over rows that reported one.</summary>
        public double TokensPerSecSum { get; set; }

        /// <summary>The number of rows that reported a tokens-per-second value.</summary>
        public long TokensPerSecCount { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageAggregateRow"/> class.
        /// </summary>
        public UsageAggregateRow()
        {
        }

        #endregion
    }
}
