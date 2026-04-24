namespace Mux.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Type of event emitted by the agent loop.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AgentEventTypeEnum
    {
        /// <summary>
        /// The run has started and effective runtime metadata is available.
        /// </summary>
        [EnumMember(Value = "run_started")]
        RunStarted,

        /// <summary>
        /// Streamed or complete text from the assistant.
        /// </summary>
        [EnumMember(Value = "assistant_text")]
        AssistantText,

        /// <summary>
        /// A tool call has been proposed by the model.
        /// </summary>
        [EnumMember(Value = "tool_call_proposed")]
        ToolCallProposed,

        /// <summary>
        /// A proposed tool call was approved for execution.
        /// </summary>
        [EnumMember(Value = "tool_call_approved")]
        ToolCallApproved,

        /// <summary>
        /// A tool call has finished executing.
        /// </summary>
        [EnumMember(Value = "tool_call_completed")]
        ToolCallCompleted,

        /// <summary>
        /// An error occurred during agent processing.
        /// </summary>
        [EnumMember(Value = "error")]
        Error,

        /// <summary>
        /// Periodic heartbeat indicating the agent is alive.
        /// </summary>
        [EnumMember(Value = "heartbeat")]
        Heartbeat,

        /// <summary>
        /// Estimated context state for the current conversation.
        /// </summary>
        [EnumMember(Value = "context_status")]
        ContextStatus,

        /// <summary>
        /// A compaction action was applied to reduce context usage.
        /// </summary>
        [EnumMember(Value = "context_compacted")]
        ContextCompacted,

        /// <summary>
        /// The run has finished and a final summary is available.
        /// </summary>
        [EnumMember(Value = "run_completed")]
        RunCompleted
    }
}
