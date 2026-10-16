namespace Mux.Core.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// Sends tool calls that need approval to an MCP permission prompt tool, the contract of Claude Code's
    /// <c>--permission-prompt-tool</c>: the tool receives <c>{"tool_name","input","tool_use_id"}</c> and answers with
    /// text holding <c>{"behavior":"allow"}</c> or <c>{"behavior":"deny","message":"..."}</c>. Anything else (an
    /// error, a timeout, an unknown tool, or an answer without a recognized behavior) denies the call, so a broken
    /// prompt never lets a tool run.
    /// </summary>
    public sealed class McpPermissionPrompter
    {
        #region Public-Members

        /// <summary>
        /// The MCP tool that answers prompts, in Mux's "server.tool" form.
        /// </summary>
        public string ToolName => _ToolName;

        #endregion

        #region Private-Members

        private const int MaxEchoedLength = 500;

        private static readonly JsonSerializerOptions _ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _ToolName;
        private readonly Func<string, JsonElement, CancellationToken, Task<ToolResult>> _Execute;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpPermissionPrompter"/> class.
        /// </summary>
        /// <param name="toolName">The prompt tool, as "server.tool" or Claude Code's "mcp__server__tool".</param>
        /// <param name="execute">Runs an MCP tool call (tool name, arguments, cancellation) and returns its result.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolName"/> is null or blank, or <paramref name="execute"/> is null.</exception>
        public McpPermissionPrompter(string toolName, Func<string, JsonElement, CancellationToken, Task<ToolResult>> execute)
        {
            if (string.IsNullOrWhiteSpace(toolName)) throw new ArgumentNullException(nameof(toolName));
            _ToolName = NormalizeToolName(toolName);
            _Execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Converts Claude Code's "mcp__server__tool" form to Mux's "server.tool" form. Other names are returned trimmed.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>The Mux tool name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolName"/> is null.</exception>
        public static string NormalizeToolName(string toolName)
        {
            if (toolName == null) throw new ArgumentNullException(nameof(toolName));
            string trimmed = toolName.Trim();
            if (!trimmed.StartsWith("mcp__", StringComparison.Ordinal)) return trimmed;

            string rest = trimmed.Substring("mcp__".Length);
            int separator = rest.IndexOf("__", StringComparison.Ordinal);
            if (separator <= 0 || separator + 2 >= rest.Length) return trimmed;
            return rest.Substring(0, separator) + "." + rest.Substring(separator + 2);
        }

        /// <summary>
        /// Asks the prompt tool whether a tool call may run.
        /// </summary>
        /// <param name="request">The tool call awaiting approval.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The decision; a failure denies.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<PermissionPromptResult> PromptAsync(ApprovalRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            PermissionPromptArguments arguments = new PermissionPromptArguments
            {
                ToolName = request.ToolName,
                Input = ParseInput(request.ArgumentsSummary),
                ToolUseId = string.IsNullOrEmpty(request.ToolCallId) ? null : request.ToolCallId
            };

            ToolResult result;
            try
            {
                result = await _Execute(_ToolName, JsonSerializer.SerializeToElement(arguments), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return PermissionPromptResult.Deny("The permission prompt tool " + _ToolName + " failed: " + ex.Message);
            }

            return ParseResult(_ToolName, result);
        }

        /// <summary>
        /// Reads the decision from a prompt tool's result (the raw MCP <c>tools/call</c> result). The decision is taken
        /// from <c>structuredContent</c> or from the first text content item that holds one.
        /// </summary>
        /// <param name="toolName">The prompt tool's name, for messages.</param>
        /// <param name="result">The tool result, or null.</param>
        /// <returns>The decision; anything unrecognized denies.</returns>
        public static PermissionPromptResult ParseResult(string toolName, ToolResult? result)
        {
            if (result == null) return PermissionPromptResult.Deny("The permission prompt tool " + toolName + " returned no result.");
            if (!result.Success) return PermissionPromptResult.Deny("The permission prompt tool " + toolName + " failed: " + Echo(result.Content));

            McpToolCallDocument? document = TryDeserialize<McpToolCallDocument>(result.Content);
            List<PermissionPromptAnswer> candidates = new List<PermissionPromptAnswer>();
            if (document?.StructuredContent != null) candidates.Add(document.StructuredContent);
            if (document?.Content != null)
            {
                foreach (McpContentItem item in document.Content)
                {
                    if (!string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(item.Text)) continue;
                    PermissionPromptAnswer? answer = TryDeserialize<PermissionPromptAnswer>(item.Text);
                    if (answer != null) candidates.Add(answer);
                }
            }

            // A bare decision object (a tool that returns the answer itself rather than an MCP result envelope).
            if (document?.Content == null && document?.StructuredContent == null)
            {
                PermissionPromptAnswer? bare = TryDeserialize<PermissionPromptAnswer>(result.Content);
                if (bare != null) candidates.Add(bare);
            }

            foreach (PermissionPromptAnswer answer in candidates)
            {
                if (string.Equals(answer.Behavior, "allow", StringComparison.OrdinalIgnoreCase)) return PermissionPromptResult.Allow();
                if (string.Equals(answer.Behavior, "deny", StringComparison.OrdinalIgnoreCase))
                    return PermissionPromptResult.Deny(string.IsNullOrWhiteSpace(answer.Message) ? "Permission was denied." : answer.Message);
            }

            return PermissionPromptResult.Deny("The permission prompt tool " + toolName + " returned no decision: " + Echo(result.Content));
        }

        #endregion

        #region Private-Methods

        private static JsonElement ParseInput(string argumentsJson)
        {
            if (!string.IsNullOrWhiteSpace(argumentsJson))
            {
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(argumentsJson);
                    if (parsed.RootElement.ValueKind == JsonValueKind.Object) return parsed.RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }

            using JsonDocument empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }

        private static T? TryDeserialize<T>(string? json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            string trimmed = json!.TrimStart();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(trimmed, _ReadOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Echo(string? content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "(empty)";
            string trimmed = content!.Trim();
            return trimmed.Length <= MaxEchoedLength ? trimmed : trimmed.Substring(0, MaxEchoedLength) + "...";
        }

        #endregion

        #region Private-Classes

        private sealed class PermissionPromptArguments
        {
            [JsonPropertyName("tool_name")]
            public string ToolName { get; set; } = string.Empty;

            [JsonPropertyName("input")]
            public JsonElement Input { get; set; }

            [JsonPropertyName("tool_use_id")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? ToolUseId { get; set; }
        }

        private sealed class McpToolCallDocument
        {
            [JsonPropertyName("content")]
            public List<McpContentItem>? Content { get; set; }

            [JsonPropertyName("structuredContent")]
            public PermissionPromptAnswer? StructuredContent { get; set; }
        }

        private sealed class McpContentItem
        {
            [JsonPropertyName("type")]
            public string? Type { get; set; }

            [JsonPropertyName("text")]
            public string? Text { get; set; }
        }

        private sealed class PermissionPromptAnswer
        {
            [JsonPropertyName("behavior")]
            public string? Behavior { get; set; }

            [JsonPropertyName("message")]
            public string? Message { get; set; }
        }

        #endregion
    }
}
