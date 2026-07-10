namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Streaming choice from an OpenAI-compatible chat API.
    /// </summary>
    public class OpenAiStreamingChoice
    {
        private string? _FinishReason = null;
        private OpenAiStreamingDelta? _Delta = null;

        /// <summary>
        /// Gets or sets the finish reason.
        /// </summary>
        [JsonPropertyName("finish_reason")]
        public string? FinishReason
        {
            get => _FinishReason;
            set => _FinishReason = value;
        }

        /// <summary>
        /// Gets or sets the streamed delta.
        /// </summary>
        [JsonPropertyName("delta")]
        public OpenAiStreamingDelta? Delta
        {
            get => _Delta;
            set => _Delta = value;
        }
    }
}
