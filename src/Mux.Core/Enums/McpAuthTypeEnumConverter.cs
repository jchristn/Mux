namespace Mux.Core.Enums
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON converter for <see cref="McpAuthTypeEnum"/> values.
    /// </summary>
    public class McpAuthTypeEnumConverter : JsonConverter<McpAuthTypeEnum>
    {
        /// <summary>
        /// Reads and converts a JSON string to an <see cref="McpAuthTypeEnum"/> value.
        /// </summary>
        public override McpAuthTypeEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return McpAuthTypeEnum.None;
            }

            string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            return normalized switch
            {
                "none" => McpAuthTypeEnum.None,
                "bearer" => McpAuthTypeEnum.Bearer,
                "apikey" => McpAuthTypeEnum.ApiKey,
                _ => throw new JsonException($"Unknown MCP auth type: '{value}'. Expected: none, bearer, or apikey.")
            };
        }

        /// <summary>
        /// Writes an <see cref="McpAuthTypeEnum"/> value as a lowercase JSON string.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, McpAuthTypeEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                McpAuthTypeEnum.None => "none",
                McpAuthTypeEnum.Bearer => "bearer",
                McpAuthTypeEnum.ApiKey => "apikey",
                _ => value.ToString().ToLowerInvariant()
            });
        }
    }
}
