namespace Mux.Core.Llm
{
    /// <summary>
    /// Performance metrics for a single streaming LLM call, projected from the provider response. Timing
    /// fields are null when the provider did not report them. Instances are produced by
    /// <see cref="LlmClient"/> and read by callers that persist usage telemetry; they are best-effort
    /// diagnostics and never affect request handling.
    /// </summary>
    public sealed class LlmCallMetrics
    {
        #region Private-Members

        private LlmUsage _Usage = new LlmUsage();

        #endregion

        #region Public-Members

        /// <summary>
        /// Provider-reported token usage for the call. Never null; fields remain zero when the provider
        /// reports no usage.
        /// </summary>
        public LlmUsage Usage
        {
            get => _Usage;
            set => _Usage = value ?? new LlmUsage();
        }

        /// <summary>
        /// Time from request start to the first text or tool-call delta, in milliseconds, or null when the
        /// provider did not report it.
        /// </summary>
        public long? TimeToFirstTokenMs { get; set; }

        /// <summary>
        /// Time from the first delta to stream completion, in milliseconds, or null when it cannot be
        /// derived (for example when no first-token time was reported).
        /// </summary>
        public long? StreamingMs { get; set; }

        /// <summary>
        /// Total request duration from request start to stream end, in milliseconds, or null when the
        /// provider did not report it.
        /// </summary>
        public long? TotalMs { get; set; }

        /// <summary>
        /// Overall output tokens per second for the call, or null when unavailable.
        /// </summary>
        public double? TokensPerSecond { get; set; }

        /// <summary>
        /// The provider's finish reason (for example stop, length, tool_calls), or null when unreported.
        /// </summary>
        public string? FinishReason { get; set; }

        /// <summary>
        /// The model identifier echoed by the provider, or null when unreported.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Whether the call completed successfully.
        /// </summary>
        public bool Success { get; set; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LlmCallMetrics"/> class.
        /// </summary>
        public LlmCallMetrics()
        {
        }

        #endregion
    }
}
