namespace Mux.Core.Llm
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Message payload used by OpenAI-compatible chat APIs.
    /// </summary>
    public class OpenAiChatMessage
    {
        private string _Role = string.Empty;
        private string? _Content = null;
        private List<OpenAiToolCall>? _ToolCalls = null;
        private string? _ToolCallId = null;

        /// <summary>
        /// Gets or sets the message role.
        /// </summary>
        [JsonPropertyName("role")]
        public string Role
        {
            get => _Role;
            set => _Role = value ?? string.Empty;
        }

        /// <summary>
        /// Gets or sets the message content.
        /// </summary>
        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Content
        {
            get => _Content;
            set => _Content = value;
        }

        /// <summary>
        /// Gets or sets assistant tool calls.
        /// </summary>
        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OpenAiToolCall>? ToolCalls
        {
            get => _ToolCalls;
            set => _ToolCalls = value;
        }

        /// <summary>
        /// Gets or sets the related tool call identifier for tool-result messages.
        /// </summary>
        [JsonPropertyName("tool_call_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ToolCallId
        {
            get => _ToolCallId;
            set => _ToolCallId = value;
        }
    }
}
