namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Tool definition payload used by OpenAI-compatible chat APIs.
    /// </summary>
    public class OpenAiToolDefinition
    {
        private string _Type = "function";
        private OpenAiFunctionDefinition _Function = new OpenAiFunctionDefinition();

        /// <summary>
        /// Gets or sets the tool definition type.
        /// </summary>
        [JsonPropertyName("type")]
        public string Type
        {
            get => _Type;
            set => _Type = value ?? "function";
        }

        /// <summary>
        /// Gets or sets the function definition.
        /// </summary>
        [JsonPropertyName("function")]
        public OpenAiFunctionDefinition Function
        {
            get => _Function;
            set => _Function = value ?? new OpenAiFunctionDefinition();
        }
    }
}
