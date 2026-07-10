namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Streaming tool call fragment from an OpenAI-compatible chat API.
    /// </summary>
    public class OpenAiStreamingToolCall
    {
        private int? _Index = null;
        private string? _Id = null;
        private OpenAiFunctionCall? _Function = null;

        /// <summary>
        /// Gets or sets the tool call stream index.
        /// </summary>
        [JsonPropertyName("index")]
        public int? Index
        {
            get => _Index;
            set => _Index = value;
        }

        /// <summary>
        /// Gets or sets the tool call identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string? Id
        {
            get => _Id;
            set => _Id = value;
        }

        /// <summary>
        /// Gets or sets the streamed function payload.
        /// </summary>
        [JsonPropertyName("function")]
        public OpenAiFunctionCall? Function
        {
            get => _Function;
            set => _Function = value;
        }
    }
}
