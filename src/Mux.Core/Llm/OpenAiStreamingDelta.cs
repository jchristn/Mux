namespace Mux.Core.Llm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Streaming message delta from an OpenAI-compatible chat API.
    /// </summary>
    public class OpenAiStreamingDelta
    {
        private string? _Content = null;
        private List<OpenAiStreamingToolCall>? _ToolCalls = null;

        /// <summary>
        /// Gets or sets streamed text content.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content
        {
            get => _Content;
            set => _Content = value;
        }

        /// <summary>
        /// Gets or sets streamed tool call fragments.
        /// </summary>
        [JsonPropertyName("tool_calls")]
        public List<OpenAiStreamingToolCall>? ToolCalls
        {
            get => _ToolCalls;
            set => _ToolCalls = value;
        }
    }
}
