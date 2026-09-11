namespace Mux.Core.Telemetry
{
    /// <summary>
    /// A read model for a single stored usage event, as shown in the dashboard's usage-history table. It is
    /// the persisted row plus its database id and its derived cost (computed at read time from the pricing
    /// table). Timing fields are null when the provider did not report them.
    /// </summary>
    public sealed class UsageEventRow
    {
        #region Public-Members

        /// <summary>The database row id.</summary>
        public long Id { get; set; }

        /// <summary>The call-completion timestamp as Unix epoch milliseconds (UTC).</summary>
        public long TimestampUnixMs { get; set; }

        /// <summary>The run correlation identifier, or null.</summary>
        public string? RunId { get; set; }

        /// <summary>The session identifier, or null.</summary>
        public string? SessionId { get; set; }

        /// <summary>The call kind (primary, compaction, subagent, chat, probe).</summary>
        public string CallKind { get; set; } = string.Empty;

        /// <summary>The command/mode, or null.</summary>
        public string? Command { get; set; }

        /// <summary>The endpoint name.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>The provider/adapter family.</summary>
        public string AdapterType { get; set; } = string.Empty;

        /// <summary>The model identifier.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>The endpoint base-URL host, or null.</summary>
        public string? BaseHost { get; set; }

        /// <summary>The project label, or null.</summary>
        public string? Project { get; set; }

        /// <summary>Input/prompt tokens.</summary>
        public int InputTokens { get; set; }

        /// <summary>Cached input tokens.</summary>
        public int CachedTokens { get; set; }

        /// <summary>Output/completion tokens.</summary>
        public int OutputTokens { get; set; }

        /// <summary>Reasoning tokens.</summary>
        public int ReasoningTokens { get; set; }

        /// <summary>Total tokens.</summary>
        public int TotalTokens { get; set; }

        /// <summary>Time-to-first-token in milliseconds, or null.</summary>
        public long? TimeToFirstTokenMs { get; set; }

        /// <summary>Streaming duration in milliseconds, or null.</summary>
        public long? StreamingMs { get; set; }

        /// <summary>Total request duration in milliseconds, or null.</summary>
        public long? TotalMs { get; set; }

        /// <summary>Output tokens per second, or null.</summary>
        public double? TokensPerSecond { get; set; }

        /// <summary>The provider finish reason, or null.</summary>
        public string? FinishReason { get; set; }

        /// <summary>Whether the call succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>The error code when the call failed, or null.</summary>
        public string? ErrorCode { get; set; }

        /// <summary>The derived cost in US dollars for this call.</summary>
        public double CostUsd { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="UsageEventRow"/> class.
        /// </summary>
        public UsageEventRow()
        {
        }

        #endregion
    }
}
