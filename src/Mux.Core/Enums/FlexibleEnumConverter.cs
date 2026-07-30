namespace Mux.Core.Enums
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A JSON converter for <see cref="AdapterTypeEnum"/> that accepts multiple string formats:
    /// enum member names ("OpenAiCompatible"), snake_case ("openai_compatible"),
    /// kebab-case ("openai-compatible"), and lowercase ("openaicompatible").
    /// </summary>
    public class AdapterTypeEnumConverter : JsonConverter<AdapterTypeEnum>
    {
        /// <summary>
        /// Reads and converts a JSON string to an <see cref="AdapterTypeEnum"/> value.
        /// </summary>
        public override AdapterTypeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return AdapterTypeEnum.Ollama;
            }

            if (!TryParse(value, out AdapterTypeEnum result))
            {
                throw new JsonException($"Unknown adapter type: '{value}'. Expected: ollama, openai, vllm, openai-compatible.");
            }

            return result;
        }

        /// <summary>
        /// Parses an adapter-type string accepting enum member names ("OpenAiCompatible"), snake_case
        /// ("openai_compatible"), kebab-case ("openai-compatible"), and lowercase ("openaicompatible").
        /// Use this anywhere an adapter-type string is accepted (JSON, CLI flags) so every surface parses
        /// the same forms.
        /// </summary>
        /// <param name="value">The adapter-type string.</param>
        /// <param name="result">The parsed value when the method returns true.</param>
        /// <returns>True when the value was recognized; otherwise false.</returns>
        public static bool TryParse(string? value, out AdapterTypeEnum result)
        {
            result = AdapterTypeEnum.Ollama;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // Normalize: lowercase, strip hyphens and underscores
            string normalized = value.Replace("-", "").Replace("_", "").ToLowerInvariant();

            switch (normalized)
            {
                case "ollama":
                    result = AdapterTypeEnum.Ollama;
                    return true;
                case "openai":
                    result = AdapterTypeEnum.OpenAi;
                    return true;
                case "vllm":
                    result = AdapterTypeEnum.Vllm;
                    return true;
                case "openaicompatible":
                    result = AdapterTypeEnum.OpenAiCompatible;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Writes an <see cref="AdapterTypeEnum"/> value as a kebab-case JSON string.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, AdapterTypeEnum value, JsonSerializerOptions options)
        {
            string output = value switch
            {
                AdapterTypeEnum.Ollama => "ollama",
                AdapterTypeEnum.OpenAi => "openai",
                AdapterTypeEnum.Vllm => "vllm",
                AdapterTypeEnum.OpenAiCompatible => "openai-compatible",
                _ => value.ToString()
            };

            writer.WriteStringValue(output);
        }
    }
}
