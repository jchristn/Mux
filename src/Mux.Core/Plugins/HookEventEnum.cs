namespace Mux.Core.Plugins
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// The lifecycle events that out-of-process hooks can subscribe to. Kept deliberately small and
    /// well-defined: each event names a concrete moment in an interactive session at which mux runs the
    /// matching hooks.
    /// </summary>
    [JsonConverter(typeof(HookEventEnumConverter))]
    public enum HookEventEnum
    {
        /// <summary>
        /// Fired once when an interactive session starts, before the first prompt.
        /// </summary>
        SessionStart,

        /// <summary>
        /// Fired when the user submits a prompt, before the turn runs. A hook that exits non-zero vetoes
        /// the submission.
        /// </summary>
        UserPromptSubmit,

        /// <summary>
        /// Fired once when an interactive session ends.
        /// </summary>
        SessionEnd
    }

    /// <summary>
    /// A JSON converter for <see cref="HookEventEnum"/> that reads and writes kebab-case names
    /// (<c>session-start</c>, <c>user-prompt-submit</c>, <c>session-end</c>) and also accepts the enum
    /// member name, snake_case, and lowercase.
    /// </summary>
    public sealed class HookEventEnumConverter : JsonConverter<HookEventEnum>
    {
        /// <inheritdoc />
        public override HookEventEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (!TryParse(value, out HookEventEnum result))
            {
                throw new JsonException($"Unknown hook event: '{value}'. Expected: session-start, user-prompt-submit, session-end.");
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

            string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
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
                _ => value.ToString()
            };
        }
    }
}
