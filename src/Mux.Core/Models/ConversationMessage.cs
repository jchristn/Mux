namespace Mux.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Mux.Core.Enums;

    /// <summary>
    /// Represents a single message in a conversation, including role, content, and optional tool calls.
    /// </summary>
    public class ConversationMessage
    {
        #region Private-Members

        private RoleEnum _Role = RoleEnum.User;
        private string? _Content = null;
        private List<ToolCall>? _ToolCalls = null;
        private string? _ToolCallId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationMessage"/> class with default values.
        /// </summary>
        public ConversationMessage()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The role of the message author.
        /// </summary>
        [JsonPropertyName("role")]
        public RoleEnum Role
        {
            get => _Role;
            set => _Role = value;
        }

        /// <summary>
        /// The text content of the message, or null if the message contains only tool calls.
        /// </summary>
        [JsonPropertyName("content")]
        public string? Content
        {
            get => _Content;
            set => _Content = value;
        }

        /// <summary>
        /// The list of tool calls requested by the assistant, or null if none.
        /// </summary>
        [JsonPropertyName("toolCalls")]
        public List<ToolCall>? ToolCalls
        {
            get => _ToolCalls;
            set => _ToolCalls = value;
        }

        /// <summary>
        /// The identifier of the tool call this message is a result for, or null if not a tool result.
        /// </summary>
        [JsonPropertyName("toolCallId")]
        public string? ToolCallId
        {
            get => _ToolCallId;
            set => _ToolCallId = value;
        }

        #endregion
    }
}
