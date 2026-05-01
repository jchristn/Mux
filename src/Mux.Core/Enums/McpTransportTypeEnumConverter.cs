namespace Mux.Core.Enums
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON converter for <see cref="McpTransportTypeEnum"/> values.
    /// </summary>
    public class McpTransportTypeEnumConverter : JsonConverter<McpTransportTypeEnum>
    {
        /// <summary>
        /// Reads and converts a JSON string to an <see cref="McpTransportTypeEnum"/> value.
        /// </summary>
        public override McpTransportTypeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return McpTransportTypeEnum.Stdio;
            }

            string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            return normalized switch
            {
                "stdio" => McpTransportTypeEnum.Stdio,
                "http" => McpTransportTypeEnum.Http,
                _ => throw new JsonException($"Unknown MCP transport: '{value}'. Expected: stdio or http.")
            };
        }

        /// <summary>
        /// Writes an <see cref="McpTransportTypeEnum"/> value as a lowercase JSON string.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, McpTransportTypeEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                McpTransportTypeEnum.Stdio => "stdio",
                McpTransportTypeEnum.Http => "http",
                _ => value.ToString().ToLowerInvariant()
            });
        }
    }
}
