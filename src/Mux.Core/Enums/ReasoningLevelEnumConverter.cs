namespace Mux.Core.Enums
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON converter for <see cref="ReasoningLevelEnum"/> that accepts case-insensitive level names
    /// ("minimal", "low", "medium", "high") and writes them back in lowercase. Use <see cref="TryParse"/>
    /// anywhere a level string is accepted (JSON, CLI flags) so every surface parses the same forms.
    /// </summary>
    public class ReasoningLevelEnumConverter : JsonConverter<ReasoningLevelEnum>
    {
        /// <summary>
        /// Reads and converts a JSON string to a <see cref="ReasoningLevelEnum"/> value.
        /// </summary>
        /// <param name="reader">The reader.</param>
        /// <param name="typeToConvert">The type being converted.</param>
        /// <param name="options">The serializer options.</param>
        /// <returns>The parsed value.</returns>
        /// <exception cref="JsonException">Thrown when the string is not a recognized level.</exception>
        public override ReasoningLevelEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (!TryParse(value, out ReasoningLevelEnum result))
            {
                throw new JsonException($"Unknown reasoning level: '{value}'. Expected: minimal, low, medium, high.");
            }

            return result;
        }

        /// <summary>
        /// Parses a reasoning-level string, accepting case-insensitive level names.
        /// </summary>
        /// <param name="value">The level string.</param>
        /// <param name="result">The parsed value when the method returns true.</param>
        /// <returns>True when the value was recognized; otherwise false.</returns>
        public static bool TryParse(string? value, out ReasoningLevelEnum result)
        {
            result = ReasoningLevelEnum.Medium;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "minimal":
                    result = ReasoningLevelEnum.Minimal;
                    return true;
                case "low":
                    result = ReasoningLevelEnum.Low;
                    return true;
                case "medium":
                    result = ReasoningLevelEnum.Medium;
                    return true;
                case "high":
                    result = ReasoningLevelEnum.High;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the lowercase wire string for a level ("minimal", "low", "medium", "high").
        /// </summary>
        /// <param name="value">The level.</param>
        /// <returns>The lowercase name.</returns>
        public static string ToWire(ReasoningLevelEnum value)
        {
            switch (value)
            {
                case ReasoningLevelEnum.Minimal: return "minimal";
                case ReasoningLevelEnum.Low: return "low";
                case ReasoningLevelEnum.Medium: return "medium";
                case ReasoningLevelEnum.High: return "high";
                default: return value.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Writes a <see cref="ReasoningLevelEnum"/> value as a lowercase JSON string.
        /// </summary>
        /// <param name="writer">The writer.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="options">The serializer options.</param>
        public override void Write(Utf8JsonWriter writer, ReasoningLevelEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ToWire(value));
        }
    }
}
