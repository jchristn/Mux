namespace Mux.Search.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;

    /// <summary>
    /// Helper methods for tolerant JSON parsing across provider responses.
    /// </summary>
    internal static class JsonElementExtensions
    {
        public static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out JsonElement value))
            {
                return value;
            }

            return null;
        }

        public static string? GetStringOrNull(this JsonElement element, string propertyName)
        {
            JsonElement? value = element.GetPropertyOrNull(propertyName);
            return value.HasValue ? GetStringValue(value.Value) : null;
        }

        public static double? GetDoubleOrNull(this JsonElement element, string propertyName)
        {
            JsonElement? value = element.GetPropertyOrNull(propertyName);
            return value.HasValue ? GetDoubleValue(value.Value) : null;
        }

        public static int? GetInt32OrNull(this JsonElement element, string propertyName)
        {
            JsonElement? value = element.GetPropertyOrNull(propertyName);
            return value.HasValue ? GetInt32Value(value.Value) : null;
        }

        public static DateTimeOffset? GetDateTimeOffsetOrNull(this JsonElement element, string propertyName)
        {
            JsonElement? value = element.GetPropertyOrNull(propertyName);
            return value.HasValue ? GetDateTimeOffsetValue(value.Value) : null;
        }

        public static List<string> GetStringListOrEmpty(this JsonElement element, string propertyName)
        {
            JsonElement? value = element.GetPropertyOrNull(propertyName);
            return value.HasValue ? GetStringListValue(value.Value) : new List<string>();
        }

        public static string? GetStringValue(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };
        }

        public static double? GetDoubleValue(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number
                && element.TryGetDouble(out double numericValue))
            {
                return numericValue;
            }

            if (element.ValueKind == JsonValueKind.String
                && double.TryParse(
                    element.GetString(),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out double parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        public static int? GetInt32Value(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number
                && element.TryGetInt32(out int numericValue))
            {
                return numericValue;
            }

            if (element.ValueKind == JsonValueKind.String
                && int.TryParse(
                    element.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        public static DateTimeOffset? GetDateTimeOffsetValue(JsonElement element)
        {
            string? value = GetStringValue(element);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        public static List<string> GetStringListValue(JsonElement element)
        {
            List<string> values = new List<string>();

            if (element.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (JsonElement item in element.EnumerateArray())
            {
                string? value = GetStringValue(item);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }

            return values;
        }
    }
}
