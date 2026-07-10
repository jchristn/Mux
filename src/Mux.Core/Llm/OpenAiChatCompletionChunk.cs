namespace Mux.Core.Llm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Streaming response chunk from an OpenAI-compatible chat API.
    /// </summary>
    public class OpenAiChatCompletionChunk
    {
        private List<OpenAiStreamingChoice> _Choices = new List<OpenAiStreamingChoice>();

        /// <summary>
        /// Gets or sets streaming choices.
        /// </summary>
        [JsonPropertyName("choices")]
        public List<OpenAiStreamingChoice> Choices
        {
            get => _Choices;
            set => _Choices = value ?? new List<OpenAiStreamingChoice>();
        }
    }
}
