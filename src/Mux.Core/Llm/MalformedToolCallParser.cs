namespace Mux.Core.Llm
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Mux.Core.Models;

    /// <summary>
    /// Attempts to extract tool calls from malformed or non-standard model output text.
    /// </summary>
    public static class MalformedToolCallParser
    {
        #region Private-Members

        private static readonly Regex _CodeBlockRegex = new Regex(
            @"```(?:json)?\s*\n?([\s\S]*?)```",
            RegexOptions.Compiled);

        private static readonly Regex _FunctionCallRegex = new Regex(
            @"(\w+)\s*\(\s*(\{[\s\S]*?\})\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex _NameArgumentsRegex = new Regex(
            @"\{\s*""name""\s*:\s*""([^""]+)""\s*,\s*""arguments""\s*:\s*(\{[\s\S]*?\})\s*\}",
            RegexOptions.Compiled);

        private static readonly Regex _NameStringArgumentsRegex = new Regex(
            @"\{\s*""name""\s*:\s*""([^""]+)""\s*,\s*""arguments""\s*:\s*""((?:\\""|[^""])*)""\s*\}",
            RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Attempts to extract tool calls from raw text that may contain malformed tool call output.
        /// </summary>
        /// <param name="text">The raw text to parse.</param>
        /// <returns>A list of extracted <see cref="ToolCall"/> instances, or null if nothing was found.</returns>
        public static List<ToolCall>? TryExtractToolCalls(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            List<ToolCall>? result = null;

            // Strategy 1: Look for JSON objects in markdown code blocks
            result = TryExtractFromCodeBlocks(text);
            if (result != null && result.Count > 0)
                return result;

            // Strategy 2: Look for patterns like tool_name({"arg": "value"})
            result = TryExtractFromFunctionCallPattern(text);
            if (result != null && result.Count > 0)
                return result;

            // Strategy 3: Regex extraction of JSON-like structures with "name" and "arguments" fields
            result = TryExtractFromNameArgumentsPattern(text);
            if (result != null && result.Count > 0)
                return result;

            return null;
        }

        #endregion

        #region Private-Methods

        private static List<ToolCall>? TryExtractFromCodeBlocks(string text)
        {
            MatchCollection matches = _CodeBlockRegex.Matches(text);
            List<ToolCall> toolCalls = new List<ToolCall>();

            foreach (Match match in matches)
            {
                string jsonContent = match.Groups[1].Value.Trim();

                List<ToolCall>? objectArgumentCalls = TryExtractFromNameArgumentsPattern(jsonContent);
                if (objectArgumentCalls != null)
                {
                    toolCalls.AddRange(objectArgumentCalls);
                }

                List<ToolCall>? stringArgumentCalls = TryExtractFromNameStringArgumentsPattern(jsonContent);
                if (stringArgumentCalls != null)
                {
                    toolCalls.AddRange(stringArgumentCalls);
                }
            }

            return toolCalls.Count > 0 ? toolCalls : null;
        }

        private static List<ToolCall>? TryExtractFromFunctionCallPattern(string text)
        {
            MatchCollection matches = _FunctionCallRegex.Matches(text);
            List<ToolCall> toolCalls = new List<ToolCall>();

            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                string argsJson = match.Groups[2].Value;

                try
                {
                    JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson);

                    ToolCall toolCall = new ToolCall
                    {
                        Id = "malformed_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                        Name = name,
                        Arguments = argsJson
                    };

                    toolCalls.Add(toolCall);
                }
                catch (JsonException)
                {
                    // Invalid JSON arguments, skip
                }
            }

            return toolCalls.Count > 0 ? toolCalls : null;
        }

        private static List<ToolCall>? TryExtractFromNameArgumentsPattern(string text)
        {
            MatchCollection matches = _NameArgumentsRegex.Matches(text);
            List<ToolCall> toolCalls = new List<ToolCall>();

            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                string argsJson = match.Groups[2].Value;

                try
                {
                    JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson);

                    ToolCall toolCall = new ToolCall
                    {
                        Id = "malformed_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                        Name = name,
                        Arguments = argsJson
                    };

                    toolCalls.Add(toolCall);
                }
                catch (JsonException)
                {
                    // Invalid JSON arguments, skip
                }
            }

            return toolCalls.Count > 0 ? toolCalls : null;
        }

        private static List<ToolCall>? TryExtractFromNameStringArgumentsPattern(string text)
        {
            MatchCollection matches = _NameStringArgumentsRegex.Matches(text);
            List<ToolCall> toolCalls = new List<ToolCall>();

            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                string escapedArguments = match.Groups[2].Value;
                string argsJson;

                try
                {
                    argsJson = JsonSerializer.Deserialize<string>("\"" + escapedArguments + "\"") ?? "{}";
                    JsonSerializer.Deserialize<Dictionary<string, object?>>(argsJson);
                }
                catch (JsonException)
                {
                    continue;
                }

                ToolCall toolCall = new ToolCall
                {
                    Id = "malformed_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                    Name = name,
                    Arguments = argsJson
                };

                toolCalls.Add(toolCall);
            }

            return toolCalls.Count > 0 ? toolCalls : null;
        }

        #endregion
    }
}
