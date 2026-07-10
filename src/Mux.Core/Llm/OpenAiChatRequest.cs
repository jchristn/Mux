namespace Mux.Core.Llm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Request payload for an OpenAI-compatible chat completion call.
    /// </summary>
    public class OpenAiChatRequest
    {
        private string _Model = string.Empty;
        private List<OpenAiChatMessage> _Messages = new List<OpenAiChatMessage>();
        private double? _Temperature = null;
        private int? _MaxTokens = null;
        private bool? _Stream = null;
        private List<OpenAiToolDefinition>? _Tools = null;
        private bool? _ParallelToolCalls = null;

        /// <summary>
        /// Gets or sets the model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model
        {
            get => _Model;
            set => _Model = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the messages sent to the model.
        /// </summary>
        [JsonPropertyName("messages")]
        public List<OpenAiChatMessage> Messages
        {
            get => _Messages;
            set => _Messages = value ?? new List<OpenAiChatMessage>();
        }

        /// <summary>
        /// Gets or sets the sampling temperature.
        /// </summary>
        [JsonPropertyName("temperature")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Temperature
        {
            get => _Temperature;
            set => _Temperature = value;
        }

        /// <summary>
        /// Gets or sets the maximum output token count.
        /// </summary>
        [JsonPropertyName("max_tokens")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? MaxTokens
        {
            get => _MaxTokens;
            set => _MaxTokens = value;
        }

        /// <summary>
        /// Gets or sets whether streaming is requested.
        /// </summary>
        [JsonPropertyName("stream")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Stream
        {
            get => _Stream;
            set => _Stream = value;
        }

        /// <summary>
        /// Gets or sets the tool definitions available to the model.
        /// </summary>
        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OpenAiToolDefinition>? Tools
        {
            get => _Tools;
            set => _Tools = value;
        }

        /// <summary>
        /// Gets or sets whether parallel tool calls are enabled.
        /// </summary>
        [JsonPropertyName("parallel_tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? ParallelToolCalls
        {
            get => _ParallelToolCalls;
            set => _ParallelToolCalls = value;
        }
    }
}
