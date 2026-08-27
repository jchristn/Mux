namespace Mux.Cli.App
{
    using System;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// Distills a failed tool call's result payload into a single concise, human-readable reason line so a
    /// failure can be diagnosed from the transcript rather than showing only a bare "✗". Handles mux's own
    /// <c>{ "error": ..., "message": ... }</c> error shape, the MCP standard <c>{ "content": [ { "text":
    /// ... } ], "isError": true }</c> payload, and arbitrary non-JSON content, collapsing whitespace and
    /// truncating over-long detail.
    /// </summary>
    public static class ToolFailureReason
    {
        #region Private-Members

        private const int MaxLength = 400;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Produces a short reason describing why a tool call failed, or null when the content carries no
        /// usable detail.
        /// </summary>
        /// <param name="content">The failed <see cref="Mux.Core.Models.ToolResult.Content"/> payload.</param>
        /// <returns>A normalized reason line, or null when nothing meaningful can be extracted.</returns>
        public static string? Describe(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string? reason = TryDescribeJson(content) ?? content;
            return Normalize(reason);
        }

        #endregion

        #region Private-Methods

        private static string? TryDescribeJson(string content)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(content);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                string? error = GetString(root, "error");
                string? message = GetString(root, "message");
                string? mcpText = ExtractMcpText(root);

                string? detail = !string.IsNullOrWhiteSpace(message)
                    ? message
                    : (!string.IsNullOrWhiteSpace(mcpText) ? mcpText : null);

                if (!string.IsNullOrWhiteSpace(error) && !string.IsNullOrWhiteSpace(detail))
                {
                    // Avoid "code: code…" when the detail already begins with the error code.
                    return detail!.StartsWith(error!, StringComparison.OrdinalIgnoreCase)
                        ? detail
                        : error + ": " + detail;
                }

                return !string.IsNullOrWhiteSpace(detail) ? detail : error;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? ExtractMcpText(JsonElement root)
        {
            if (!root.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder();
            foreach (JsonElement block in content.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object
                    && block.TryGetProperty("text", out JsonElement textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    string? text = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(' ');
                        }

                        sb.Append(text);
                    }
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out JsonElement element) && element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }

            return null;
        }

        private static string? Normalize(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return null;
            }

            // Collapse any run of whitespace (including newlines) into a single space so the reason reads as
            // one line regardless of how the tool formatted its payload.
            StringBuilder sb = new StringBuilder(reason.Length);
            bool lastWasSpace = false;
            foreach (char c in reason)
            {
                if (char.IsWhiteSpace(c))
                {
                    if (!lastWasSpace && sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }

            string collapsed = sb.ToString().Trim();
            if (collapsed.Length == 0)
            {
                return null;
            }

            return collapsed.Length <= MaxLength ? collapsed : collapsed.Substring(0, MaxLength) + "…";
        }

        #endregion
    }
}
