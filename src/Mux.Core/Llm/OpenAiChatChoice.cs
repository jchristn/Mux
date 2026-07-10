namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Non-streaming choice from an OpenAI-compatible chat API.
    /// </summary>
    public class OpenAiChatChoice
    {
        private OpenAiChatMessage? _Message = null;

        /// <summary>
        /// Gets or sets the response message.
        /// </summary>
        [JsonPropertyName("message")]
        public OpenAiChatMessage? Message
        {
            get => _Message;
            set => _Message = value;
        }
    }
}
