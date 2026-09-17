namespace Mux.Core.Enums
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// JSON converter for <see cref="AuthPlacementEnum"/> values.
    /// </summary>
    public class AuthPlacementEnumConverter : JsonConverter<AuthPlacementEnum>
    {
        /// <summary>
        /// Reads and converts a JSON string to an <see cref="AuthPlacementEnum"/> value.
        /// </summary>
        public override AuthPlacementEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? value = reader.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return AuthPlacementEnum.Bearer;
            }

            string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            return normalized switch
            {
                "bearer" => AuthPlacementEnum.Bearer,
                "bearertoken" => AuthPlacementEnum.Bearer,
                "header" => AuthPlacementEnum.Header,
                "customheader" => AuthPlacementEnum.Header,
                "query" => AuthPlacementEnum.Query,
                "querystring" => AuthPlacementEnum.Query,
                "queryparam" => AuthPlacementEnum.Query,
                _ => throw new JsonException($"Unknown auth placement: '{value}'. Expected: bearer, header, or query.")
            };
        }

        /// <summary>
        /// Parses a placement string (accepting hyphen/underscore aliases and any letter case) to an
        /// <see cref="AuthPlacementEnum"/>, returning <see cref="AuthPlacementEnum.Bearer"/> for a blank or
        /// unrecognized value. Shared by non-JSON mapping layers (REST DTOs, desktop/TUI forms).
        /// </summary>
        /// <param name="value">The placement string to parse.</param>
        /// <returns>The parsed placement, or <see cref="AuthPlacementEnum.Bearer"/> when blank/unknown.</returns>
        public static AuthPlacementEnum Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return AuthPlacementEnum.Bearer;
            }

            string normalized = value.Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
            return normalized switch
            {
                "header" => AuthPlacementEnum.Header,
                "customheader" => AuthPlacementEnum.Header,
                "query" => AuthPlacementEnum.Query,
                "querystring" => AuthPlacementEnum.Query,
                "queryparam" => AuthPlacementEnum.Query,
                _ => AuthPlacementEnum.Bearer
            };
        }

        /// <summary>
        /// Returns the canonical lowercase wire string (<c>bearer</c>, <c>header</c>, or <c>query</c>) for a
        /// placement value. Shared by non-JSON mapping layers.
        /// </summary>
        /// <param name="value">The placement value.</param>
        /// <returns>The canonical wire string.</returns>
        public static string ToWire(AuthPlacementEnum value)
        {
            return value switch
            {
                AuthPlacementEnum.Header => "header",
                AuthPlacementEnum.Query => "query",
                _ => "bearer"
            };
        }

        /// <summary>
        /// Writes an <see cref="AuthPlacementEnum"/> value as a lowercase JSON string.
        /// </summary>
        public override void Write(Utf8JsonWriter writer, AuthPlacementEnum value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value switch
            {
                AuthPlacementEnum.Bearer => "bearer",
                AuthPlacementEnum.Header => "header",
                AuthPlacementEnum.Query => "query",
                _ => value.ToString().ToLowerInvariant()
            });
        }
    }
}
