namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Role of a message participant in a conversation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RoleEnum
    {
        /// <summary>
        /// System-level instruction or context.
        /// </summary>
        [EnumMember(Value = "system")]
        System,

        /// <summary>
        /// Message from the user.
        /// </summary>
        [EnumMember(Value = "user")]
        User,

        /// <summary>
        /// Message from the assistant.
        /// </summary>
        [EnumMember(Value = "assistant")]
        Assistant,

        /// <summary>
        /// Message representing a tool invocation or result.
        /// </summary>
        [EnumMember(Value = "tool")]
        Tool
    }
}
