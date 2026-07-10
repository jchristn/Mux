namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tool call payload used by OpenAI-compatible chat APIs.
    /// </summary>
    public class OpenAiToolCall
    {
        private string _Id = string.Empty;
        private string _Type = "function";
        private OpenAiFunctionCall _Function = new OpenAiFunctionCall();

        /// <summary>
        /// Gets or sets the tool call identifier.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the tool call type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type
        {
            get => _Type;
            set => _Type = value ?? "function";
        }

        /// <summary>
        /// Gets or sets the function call payload.
        /// </summary>
        [JsonPropertyName("function")]
        public OpenAiFunctionCall Function
        {
            get => _Function;
            set => _Function = value ?? new OpenAiFunctionCall();
        }
    }
}
