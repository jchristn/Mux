namespace Mux.Core.Plugins
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON converter for <see cref="HookEventEnum"/> that reads and writes kebab-case names
    /// (<c>session-start</c>, <c>user-prompt-submit</c>, <c>session-end</c>, <c>pre-tool-use</c>,
    /// <c>post-tool-use</c>, <c>stop</c>) and also accepts the enum member name, snake_case, and lowercase.
    /// </summary>
    public sealed class HookEventEnumConverter : JsonConverter<HookEventEnum>
    {
        #region Public-Members

        /// <summary>
        /// The accepted wire names, for error messages and listings.
        /// </summary>
        public const string WireNames = "session-start, user-prompt-submit, session-end, pre-tool-use, post-tool-use, stop";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override HookEventEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (!TryParse(value, out HookEventEnum result))
            {
                throw new JsonException($"Unknown hook event: '{value}'. Expected: {WireNames}.");
            }

            return result;
        }

        /// <summary>
        /// Parses a hook-event string accepting kebab-case, snake_case, the enum member name, and lowercase.
        /// </summary>
        /// <param name="value">The hook-event string.</param>
        /// <param name="result">The parsed value when the method returns true.</param>
        /// <returns>True when the value was recognized; otherwise false.</returns>
        public static bool TryParse(string? value, out HookEventEnum result)
        {
            result = HookEventEnum.SessionStart;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            switch (normalized)
            {
                case "sessionstart":
                    result = HookEventEnum.SessionStart;
                    return true;
                case "userpromptsubmit":
                case "promptsubmit":
                    result = HookEventEnum.UserPromptSubmit;
                    return true;
                case "sessionend":
                    result = HookEventEnum.SessionEnd;
                    return true;
                case "pretooluse":
                    result = HookEventEnum.PreToolUse;
                    return true;
                case "posttooluse":
                    result = HookEventEnum.PostToolUse;
                    return true;
                case "stop":
                    result = HookEventEnum.Stop;
                    return true;
                default:
                    return false;
            }
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, HookEventEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ToWireName(value));
        }

        /// <summary>
        /// Returns the canonical kebab-case wire name for a hook event.
        /// </summary>
        /// <param name="value">The hook event.</param>
        /// <returns>The kebab-case name.</returns>
        public static string ToWireName(HookEventEnum value)
        {
            return value switch
            {
                HookEventEnum.SessionStart => "session-start",
                HookEventEnum.UserPromptSubmit => "user-prompt-submit",
                HookEventEnum.SessionEnd => "session-end",
                HookEventEnum.PreToolUse => "pre-tool-use",
                HookEventEnum.PostToolUse => "post-tool-use",
                HookEventEnum.Stop => "stop",
                _ => value.ToString()
            };
        }

        #endregion
    }
}
