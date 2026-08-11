namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Lightweight, dependency-free validation of a model's final response against a JSON Schema for
    /// <c>mux print --output-schema</c>. mux instructs the model to conform (a prompt directive) and this
    /// validator confirms the response is a single JSON value of the schema's declared top-level
    /// <c>type</c> and, for object schemas, that every <c>required</c> property is present. It is a
    /// structural gate, not a full JSON Schema implementation: nested constraints, formats, and value
    /// bounds are not enforced, keeping mux backend-agnostic and free of a schema-validation dependency.
    /// </summary>
    public static class OutputSchemaValidator
    {
        /// <summary>
        /// Determines whether a schema file's contents are themselves valid JSON.
        /// </summary>
        /// <param name="schemaJson">The raw schema text. Must not be null.</param>
        /// <param name="error">The parse error message when the method returns false.</param>
        /// <returns><c>true</c> when the schema is parseable JSON; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="schemaJson"/> is null.</exception>
        public static bool IsValidSchemaDocument(string schemaJson, out string error)
        {
            if (schemaJson is null) throw new ArgumentNullException(nameof(schemaJson));

            try
            {
                using (JsonDocument.Parse(schemaJson))
                {
                    error = string.Empty;
                    return true;
                }
            }
            catch (JsonException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Validates a model response against the schema. Returns null when the response conforms, or a
        /// human-readable reason when it does not.
        /// </summary>
        /// <param name="schemaJson">The JSON Schema text. Must not be null.</param>
        /// <param name="responseText">The model's final assistant text. Null is treated as empty.</param>
        /// <returns>Null when valid; otherwise a description of the first violation found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="schemaJson"/> is null.</exception>
        public static string? Validate(string schemaJson, string? responseText)
        {
            if (schemaJson is null) throw new ArgumentNullException(nameof(schemaJson));

            string candidate = StripCodeFences(responseText ?? string.Empty).Trim();
            if (candidate.Length == 0)
            {
                return "the response was empty; expected a JSON value conforming to the output schema.";
            }

            JsonDocument responseDoc;
            try
            {
                responseDoc = JsonDocument.Parse(candidate);
            }
            catch (JsonException ex)
            {
                return $"the response is not valid JSON: {ex.Message}";
            }

            using (responseDoc)
            {
                JsonDocument schemaDoc;
                try
                {
                    schemaDoc = JsonDocument.Parse(schemaJson);
                }
                catch (JsonException)
                {
                    // An unparseable schema is caught earlier via IsValidSchemaDocument; treat a valid JSON
                    // response as acceptable here rather than failing the run on a bad schema.
                    return null;
                }

                using (schemaDoc)
                {
                    return ValidateNode(schemaDoc.RootElement, responseDoc.RootElement, "$");
                }
            }
        }

        // Recursively validates a value against a schema node. Enforces the widely-used JSON Schema
        // keywords: type (including union type arrays and integer), enum, required, properties (recursed),
        // and array items (recursed). Value-level constraints such as numeric bounds, string patterns, and
        // formats are not enforced. Returns null when the subtree conforms, or the first violation found.
        private static string? ValidateNode(JsonElement schema, JsonElement value, string path)
        {
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (schema.TryGetProperty("type", out JsonElement typeElement) && !TypeMatchesSpec(typeElement, value.ValueKind))
            {
                return $"{path} is a {DescribeKind(value.ValueKind)} but the schema requires type {DescribeExpectedType(typeElement)}.";
            }

            if (schema.TryGetProperty("enum", out JsonElement enumElement) && enumElement.ValueKind == JsonValueKind.Array)
            {
                bool matched = false;
                foreach (JsonElement allowed in enumElement.EnumerateArray())
                {
                    if (DeepEquals(allowed, value))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    return $"{path} is not one of the values allowed by the schema's enum.";
                }
            }

            if (value.ValueKind == JsonValueKind.Object)
            {
                if (schema.TryGetProperty("required", out JsonElement requiredElement) && requiredElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement requiredProp in requiredElement.EnumerateArray())
                    {
                        if (requiredProp.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        string propName = requiredProp.GetString() ?? string.Empty;
                        if (!value.TryGetProperty(propName, out JsonElement _))
                        {
                            return $"{path} is missing required property '{propName}'.";
                        }
                    }
                }

                if (schema.TryGetProperty("properties", out JsonElement propsElement) && propsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty prop in propsElement.EnumerateObject())
                    {
                        if (value.TryGetProperty(prop.Name, out JsonElement propValue))
                        {
                            string childPath = path == "$" ? $"$.{prop.Name}" : $"{path}.{prop.Name}";
                            string? childReason = ValidateNode(prop.Value, propValue, childPath);
                            if (childReason != null)
                            {
                                return childReason;
                            }
                        }
                    }
                }
            }

            if (value.ValueKind == JsonValueKind.Array
                && schema.TryGetProperty("items", out JsonElement itemsElement)
                && itemsElement.ValueKind == JsonValueKind.Object)
            {
                int index = 0;
                foreach (JsonElement element in value.EnumerateArray())
                {
                    string? itemReason = ValidateNode(itemsElement, element, $"{path}[{index}]");
                    if (itemReason != null)
                    {
                        return itemReason;
                    }

                    index++;
                }
            }

            return null;
        }

        private static bool TypeMatchesSpec(JsonElement typeElement, JsonValueKind kind)
        {
            if (typeElement.ValueKind == JsonValueKind.String)
            {
                return TypeMatches(typeElement.GetString() ?? string.Empty, kind);
            }

            if (typeElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement candidate in typeElement.EnumerateArray())
                {
                    if (candidate.ValueKind == JsonValueKind.String && TypeMatches(candidate.GetString() ?? string.Empty, kind))
                    {
                        return true;
                    }
                }

                return false;
            }

            // A non-string, non-array type declaration is not something this validator enforces.
            return true;
        }

        private static string DescribeExpectedType(JsonElement typeElement)
        {
            if (typeElement.ValueKind == JsonValueKind.String)
            {
                return $"'{typeElement.GetString()}'";
            }

            if (typeElement.ValueKind == JsonValueKind.Array)
            {
                List<string> names = new List<string>();
                foreach (JsonElement candidate in typeElement.EnumerateArray())
                {
                    if (candidate.ValueKind == JsonValueKind.String)
                    {
                        names.Add($"'{candidate.GetString()}'");
                    }
                }

                return string.Join(" or ", names);
            }

            return "the declared type";
        }

        private static bool TypeMatches(string expectedType, JsonValueKind kind)
        {
            switch (expectedType.Trim().ToLowerInvariant())
            {
                case "object":
                    return kind == JsonValueKind.Object;
                case "array":
                    return kind == JsonValueKind.Array;
                case "string":
                    return kind == JsonValueKind.String;
                case "boolean":
                    return kind == JsonValueKind.True || kind == JsonValueKind.False;
                case "number":
                case "integer":
                    return kind == JsonValueKind.Number;
                case "null":
                    return kind == JsonValueKind.Null;
                default:
                    // An unrecognized type name is not enforced.
                    return true;
            }
        }

        private static bool DeepEquals(JsonElement a, JsonElement b)
        {
            if (a.ValueKind != b.ValueKind)
            {
                return false;
            }

            switch (a.ValueKind)
            {
                case JsonValueKind.String:
                    return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
                case JsonValueKind.Number:
                    return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
                case JsonValueKind.Object:
                    return ObjectDeepEquals(a, b);
                case JsonValueKind.Array:
                    return ArrayDeepEquals(a, b);
                default:
                    // True, False, and Null are fully determined by ValueKind.
                    return true;
            }
        }

        private static bool ObjectDeepEquals(JsonElement a, JsonElement b)
        {
            int countA = 0;
            foreach (JsonProperty prop in a.EnumerateObject())
            {
                countA++;
                if (!b.TryGetProperty(prop.Name, out JsonElement other) || !DeepEquals(prop.Value, other))
                {
                    return false;
                }
            }

            int countB = 0;
            foreach (JsonProperty _ in b.EnumerateObject())
            {
                countB++;
            }

            return countA == countB;
        }

        private static bool ArrayDeepEquals(JsonElement a, JsonElement b)
        {
            List<JsonElement> itemsA = new List<JsonElement>(a.EnumerateArray());
            List<JsonElement> itemsB = new List<JsonElement>(b.EnumerateArray());
            if (itemsA.Count != itemsB.Count)
            {
                return false;
            }

            for (int i = 0; i < itemsA.Count; i++)
            {
                if (!DeepEquals(itemsA[i], itemsB[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string DescribeKind(JsonValueKind kind)
        {
            switch (kind)
            {
                case JsonValueKind.Object: return "object";
                case JsonValueKind.Array: return "array";
                case JsonValueKind.String: return "string";
                case JsonValueKind.Number: return "number";
                case JsonValueKind.True:
                case JsonValueKind.False: return "boolean";
                case JsonValueKind.Null: return "null";
                default: return "value";
            }
        }

        private static string StripCodeFences(string text)
        {
            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstNewline = trimmed.IndexOf('\n');
            if (firstNewline < 0)
            {
                return trimmed;
            }

            // Drop the opening fence line (which may carry a language tag such as ```json).
            string body = trimmed.Substring(firstNewline + 1);
            int closingFence = body.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                body = body.Substring(0, closingFence);
            }

            return body.Trim();
        }
    }
}
