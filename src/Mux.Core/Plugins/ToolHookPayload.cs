namespace Mux.Core.Plugins
{
    using System.IO;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Builds the JSON documents delivered on stdin to <c>pre-tool-use</c>, <c>post-tool-use</c>, and <c>stop</c> hooks.
    /// Field names follow Claude Code's hook contract (<c>hook_event_name</c>, <c>session_id</c>, <c>cwd</c>,
    /// <c>tool_name</c>, <c>tool_input</c>, <c>tool_response</c>, <c>stop_hook_active</c>), so hooks written for it
    /// read the same fields here.
    /// </summary>
    public static class ToolHookPayload
    {
        #region Public-Methods

        /// <summary>
        /// Builds the payload for a tool hook.
        /// </summary>
        /// <param name="hookEventName">The event name, <c>PreToolUse</c> or <c>PostToolUse</c>.</param>
        /// <param name="sessionId">The session id, or null.</param>
        /// <param name="workingDirectory">The working directory, or null.</param>
        /// <param name="toolName">The tool name.</param>
        /// <param name="toolCallId">The tool call id.</param>
        /// <param name="argumentsJson">The tool arguments as the model sent them. Valid JSON is embedded as-is; anything else is sent as a string.</param>
        /// <param name="success">For <c>PostToolUse</c>, whether the tool succeeded; null omits <c>tool_response</c>.</param>
        /// <param name="content">For <c>PostToolUse</c>, the tool result content.</param>
        /// <returns>The JSON payload.</returns>
        public static string ForTool(string hookEventName, string? sessionId, string? workingDirectory, string toolName, string toolCallId, string? argumentsJson, bool? success, string? content)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("hook_event_name", hookEventName);
                    writer.WriteString("session_id", sessionId ?? string.Empty);
                    writer.WriteString("cwd", workingDirectory ?? string.Empty);
                    writer.WriteString("tool_name", toolName ?? string.Empty);
                    writer.WriteString("tool_call_id", toolCallId ?? string.Empty);
                    writer.WritePropertyName("tool_input");
                    WriteArguments(writer, argumentsJson);
                    if (success.HasValue)
                    {
                        writer.WritePropertyName("tool_response");
                        writer.WriteStartObject();
                        writer.WriteBoolean("success", success.Value);
                        writer.WriteString("content", content ?? string.Empty);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>
        /// Builds the payload for a stop hook.
        /// </summary>
        /// <param name="sessionId">The session id, or null.</param>
        /// <param name="workingDirectory">The working directory, or null.</param>
        /// <param name="stopHookActive">True when a stop hook already asked the model to continue in this run.</param>
        /// <param name="lastAssistantMessage">The model's final text, or null.</param>
        /// <returns>The JSON payload.</returns>
        public static string ForStop(string? sessionId, string? workingDirectory, bool stopHookActive, string? lastAssistantMessage)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
                {
                    writer.WriteStartObject();
                    writer.WriteString("hook_event_name", "Stop");
                    writer.WriteString("session_id", sessionId ?? string.Empty);
                    writer.WriteString("cwd", workingDirectory ?? string.Empty);
                    writer.WriteBoolean("stop_hook_active", stopHookActive);
                    writer.WriteString("last_assistant_message", lastAssistantMessage ?? string.Empty);
                    writer.WriteEndObject();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        #endregion

        #region Private-Methods

        private static void WriteArguments(Utf8JsonWriter writer, string? argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
                return;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(argumentsJson))
                {
                    document.RootElement.WriteTo(writer);
                }
            }
            catch (JsonException)
            {
                writer.WriteStringValue(argumentsJson);
            }
        }

        #endregion
    }
}
