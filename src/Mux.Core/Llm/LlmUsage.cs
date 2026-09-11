namespace Mux.Core.Llm
{
    /// <summary>
    /// Provider-reported token usage for one or more LLM calls. Populated from the backend's usage
    /// metadata when available; fields remain zero for providers or responses that do not report usage.
    /// </summary>
    public sealed class LlmUsage
    {
        /// <summary>
        /// Prompt/input tokens consumed.
        /// </summary>
        public int InputTokens { get; set; }

        /// <summary>
        /// Completion/output tokens generated.
        /// </summary>
        public int OutputTokens { get; set; }

        /// <summary>
        /// Cached/cache-read prompt tokens reported by the provider (a subset of the prompt that was
        /// served from the provider's prompt cache). Remains zero for providers or library versions that
        /// do not report cache usage.
        /// </summary>
        public int CachedTokens { get; set; }

        /// <summary>
        /// Reasoning/thinking tokens reported by the provider for models that bill them separately.
        /// Remains zero when the provider does not report a reasoning-token count.
        /// </summary>
        public int ReasoningTokens { get; set; }

        /// <summary>
        /// Total tokens (input + output) as reported by the provider.
        /// </summary>
        public int TotalTokens { get; set; }

        /// <summary>
        /// Adds another usage record into this one (used to accumulate usage across the iterations of a
        /// single agent run).
        /// </summary>
        /// <param name="other">The usage to add. Ignored when null.</param>
        public void Add(LlmUsage? other)
        {
            if (other == null)
            {
                return;
            }

            InputTokens += other.InputTokens;
            OutputTokens += other.OutputTokens;
            CachedTokens += other.CachedTokens;
            ReasoningTokens += other.ReasoningTokens;
            TotalTokens += other.TotalTokens;
        }
    }
}
