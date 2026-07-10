namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Function call payload used by OpenAI-compatible tool calls.
    /// </summary>
    public class OpenAiFunctionCall
    {
        private string _Name = string.Empty;
        private string _Arguments = string.Empty;

        /// <summary>
        /// Gets or sets the function name.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the serialized function arguments.
        /// </summary>
        [JsonPropertyName("arguments")]
        public string Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? string.Empty;
        }
    }
}
