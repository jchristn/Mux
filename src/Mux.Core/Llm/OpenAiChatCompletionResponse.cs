namespace Mux.Core.Llm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Non-streaming response from an OpenAI-compatible chat API.
    /// </summary>
    public class OpenAiChatCompletionResponse
    {
        private List<OpenAiChatChoice> _Choices = new List<OpenAiChatChoice>();

        /// <summary>
        /// Gets or sets response choices.
        /// </summary>
        [JsonPropertyName("choices")]
        public List<OpenAiChatChoice> Choices
        {
            get => _Choices;
            set => _Choices = value ?? new List<OpenAiChatChoice>();
        }
    }
}
