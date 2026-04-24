namespace Mux.Core.Agent
{
    /// <summary>
    /// Summarizes estimated context window usage for a conversation.
    /// </summary>
    public class ContextBudgetSnapshot
    {
        /// <summary>
        /// Total configured context window for the endpoint.
        /// </summary>
        public int ContextWindowSize { get; set; }

        /// <summary>
        /// Tokens reserved for the model's output budget.
        /// </summary>
        public int ReservedOutputTokens { get; set; }

        /// <summary>
        /// Tokens reserved as a safety margin.
        /// </summary>
        public int SafetyMarginTokens { get; set; }

        /// <summary>
        /// Effective usable input limit after reservations.
        /// </summary>
        public int UsableInputLimit { get; set; }

        /// <summary>
        /// Estimated tokens consumed by the system prompt.
        /// </summary>
        public int SystemPromptTokens { get; set; }

        /// <summary>
        /// Estimated tokens consumed by conversation history.
        /// </summary>
        public int MessageTokens { get; set; }

        /// <summary>
        /// Estimated tokens consumed by tool definitions.
        /// </summary>
        public int ToolTokens { get; set; }

        /// <summary>
        /// Total estimated tokens used against the usable input budget.
        /// </summary>
        public int UsedTokens { get; set; }

        /// <summary>
        /// Estimated remaining tokens before reaching the usable input limit.
        /// </summary>
        public int RemainingTokens { get; set; }

        /// <summary>
        /// Estimated number of tokens beyond the usable input limit.
        /// </summary>
        public int OverflowTokens { get; set; }

        /// <summary>
        /// The warning threshold used to flag approaching-context conditions.
        /// </summary>
        public int WarningThresholdTokens { get; set; }

        /// <summary>
        /// Whether the used budget is approaching the warning threshold.
        /// </summary>
        public bool IsApproachingLimit { get; set; }

        /// <summary>
        /// Whether the used budget exceeds the usable input limit.
        /// </summary>
        public bool IsOverLimit { get; set; }
    }
}
