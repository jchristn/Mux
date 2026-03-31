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

            // Normalize: lowercase, strip hyphens and underscores
            string normalized = value.Replace("-", "").Replace("_", "").ToLowerInvariant();

            switch (normalized)
            {
                case "ollama":
                    return AdapterTypeEnum.Ollama;
                case "openai":
                    return AdapterTypeEnum.OpenAi;
                case "vllm":
                    return AdapterTypeEnum.Vllm;
                case "openaicompatible":
                    return AdapterTypeEnum.OpenAiCompatible;
                default:
                    throw new JsonException($"Unknown adapter type: '{value}'. Expected: ollama, openai, vllm, openai-compatible.");
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
