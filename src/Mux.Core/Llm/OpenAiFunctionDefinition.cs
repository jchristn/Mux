namespace Mux.Core.Llm
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Function definition payload used by OpenAI-compatible tool definitions.
    /// </summary>
    public class OpenAiFunctionDefinition
    {
        private string _Name = string.Empty;
        private string _Description = string.Empty;
        private object _Parameters = new object();

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
        /// Gets or sets the function description.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the JSON schema object for function parameters.
        /// </summary>
        [JsonPropertyName("parameters")]
        public object Parameters
        {
            get => _Parameters;
            set => _Parameters = value ?? new object();
        }
    }
}
