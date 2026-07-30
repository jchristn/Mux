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
            TotalTokens += other.TotalTokens;
        }
    }
}
