namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Converts mux runtime objects into machine-readable structured output.
    /// </summary>
    public static class StructuredOutputFormatter
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serializes an <see cref="AgentEvent"/> into a JSONL-safe line.
        /// </summary>
        public static string FormatEvent(AgentEvent agentEvent)
        {
            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                ["eventType"] = GetEventTypeName(agentEvent.EventType),
                ["timestampUtc"] = agentEvent.TimestampUtc
            };

            switch (agentEvent)
            {
                case RunStartedEvent startedEvent:
                    payload["runId"] = startedEvent.RunId;
                    payload["endpointName"] = startedEvent.EndpointName;
                    payload["adapterType"] = startedEvent.AdapterType;
                    payload["baseUrl"] = startedEvent.BaseUrl;
                    payload["model"] = startedEvent.Model;
                    payload["approvalPolicy"] = startedEvent.ApprovalPolicy;
                    payload["workingDirectory"] = startedEvent.WorkingDirectory;
                    payload["maxIterations"] = startedEvent.MaxIterations;
                    payload["toolsEnabled"] = startedEvent.ToolsEnabled;
                    break;

                case AssistantTextEvent textEvent:
                    payload["text"] = RedactString(textEvent.Text);
                    break;

                case ToolCallProposedEvent proposedEvent:
                    payload["toolCall"] = FormatToolCall(proposedEvent.ToolCall);
                    break;

                case ToolCallApprovedEvent approvedEvent:
                    payload["toolCallId"] = approvedEvent.ToolCallId;
                    break;

                case ToolCallCompletedEvent completedEvent:
                    payload["toolCallId"] = completedEvent.ToolCallId;
                    payload["toolName"] = completedEvent.ToolName;
                    payload["elapsedMs"] = completedEvent.ElapsedMs;
                    payload["result"] = FormatToolResult(completedEvent.Result);
                    break;

                case ErrorEvent errorEvent:
                    payload["code"] = errorEvent.Code;
                    payload["message"] = RedactString(errorEvent.Message);
                    break;

                case HeartbeatEvent heartbeatEvent:
                    payload["stepNumber"] = heartbeatEvent.StepNumber;
                    break;

                case RunCompletedEvent runCompletedEvent:
                    payload["runId"] = runCompletedEvent.RunId;
                    payload["status"] = runCompletedEvent.Status;
                    payload["iterationsCompleted"] = runCompletedEvent.IterationsCompleted;
                    payload["toolCallCount"] = runCompletedEvent.ToolCallCount;
                    payload["errorCount"] = runCompletedEvent.ErrorCount;
                    payload["assistantTextChars"] = runCompletedEvent.AssistantTextChars;
                    payload["durationMs"] = runCompletedEvent.DurationMs;
                    break;
            }

            return JsonSerializer.Serialize(payload, _JsonOptions);
        }

        /// <summary>
        /// Serializes an arbitrary object as compact JSON.
        /// </summary>
        public static string FormatObject(object value)
        {
            return JsonSerializer.Serialize(value, _JsonOptions);
        }

        /// <summary>
        /// Creates an error event suitable for CLI bootstrap failures.
        /// </summary>
        public static ErrorEvent CreateErrorEvent(string code, string message)
        {
            return new ErrorEvent
            {
                Code = code,
                Message = message
            };
        }

        #endregion

        #region Private-Methods

        private static object FormatToolCall(ToolCall toolCall)
        {
            return new Dictionary<string, object?>
            {
                ["id"] = toolCall.Id,
                ["name"] = toolCall.Name,
                ["arguments"] = ParseAndRedactJson(toolCall.Arguments) ?? RedactString(toolCall.Arguments)
            };
        }

        private static object FormatToolResult(ToolResult toolResult)
        {
            return new Dictionary<string, object?>
            {
                ["toolCallId"] = toolResult.ToolCallId,
                ["success"] = toolResult.Success,
                ["content"] = ParseAndRedactJson(toolResult.Content) ?? RedactString(toolResult.Content)
            };
        }

        private static object? ParseAndRedactJson(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            try
            {
                JsonNode? node = JsonNode.Parse(value);
                if (node == null)
                {
                    return null;
                }

                RedactNode(node, parentPropertyName: null);
                return JsonSerializer.Deserialize<object>(node.ToJsonString(), _JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static void RedactNode(JsonNode node, string? parentPropertyName)
        {
            if (node is JsonObject obj)
            {
                List<string> propertyNames = new List<string>();
                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    propertyNames.Add(pair.Key);
                }

                foreach (string propertyName in propertyNames)
                {
                    JsonNode? childNode = obj[propertyName];
                    if (childNode == null)
                    {
                        continue;
                    }

                    if (IsSensitiveKey(propertyName))
                    {
                        obj[propertyName] = "***REDACTED***";
                        continue;
                    }

                    RedactNode(childNode, propertyName);
                }

                return;
            }

            if (node is JsonArray array)
            {
                foreach (JsonNode? child in array)
                {
                    if (child != null)
                    {
                        RedactNode(child, parentPropertyName);
                    }
                }

                return;
            }

            if (node is JsonValue jsonValue)
            {
                string? stringValue = jsonValue.TryGetValue<string>(out string? directValue)
                    ? directValue
                    : null;

                if (stringValue != null)
                {
                    if (IsSensitiveKey(parentPropertyName))
                    {
                        ReplaceJsonValue(node, "***REDACTED***");
                    }
                    else
                    {
                        ReplaceJsonValue(node, RedactString(stringValue));
                    }
                }
            }
        }

        private static void ReplaceJsonValue(JsonNode node, string value)
        {
            JsonNode? parent = node.Parent;
            if (parent is JsonArray parentArray)
            {
                for (int i = 0; i < parentArray.Count; i++)
                {
                    if (ReferenceEquals(parentArray[i], node))
                    {
                        parentArray[i] = value;
                        break;
                    }
                }
            }
            else if (parent is JsonObject parentObject)
            {
                List<string> propertyNames = new List<string>();
                foreach (KeyValuePair<string, JsonNode?> pair in parentObject)
                {
                    propertyNames.Add(pair.Key);
                }

                foreach (string propertyName in propertyNames)
                {
                    if (ReferenceEquals(parentObject[propertyName], node))
                    {
                        parentObject[propertyName] = value;
                        break;
                    }
                }
            }
        }

        private static string RedactString(string? value)
        {
            string result = value ?? string.Empty;
            result = Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_\-\.=]+", "Bearer ***REDACTED***", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bsk-[A-Za-z0-9]+\b", "***REDACTED***", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\b(x-api-key|api-key|authorization)\s*[:=]\s*[^\s,;]+", "$1=***REDACTED***", RegexOptions.IgnoreCase);
            return result;
        }

        private static bool IsSensitiveKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            string normalized = key.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .ToLowerInvariant();

            return normalized.Contains("authorization", StringComparison.Ordinal)
                || normalized.Contains("apikey", StringComparison.Ordinal)
                || normalized.Contains("token", StringComparison.Ordinal)
                || normalized.Contains("secret", StringComparison.Ordinal)
                || normalized.Contains("password", StringComparison.Ordinal);
        }

        private static string GetEventTypeName(AgentEventTypeEnum eventType)
        {
            return eventType switch
            {
                AgentEventTypeEnum.RunStarted => "run_started",
                AgentEventTypeEnum.AssistantText => "assistant_text",
                AgentEventTypeEnum.ToolCallProposed => "tool_call_proposed",
                AgentEventTypeEnum.ToolCallApproved => "tool_call_approved",
                AgentEventTypeEnum.ToolCallCompleted => "tool_call_completed",
                AgentEventTypeEnum.Error => "error",
                AgentEventTypeEnum.Heartbeat => "heartbeat",
                AgentEventTypeEnum.RunCompleted => "run_completed",
                _ => eventType.ToString()
            };
        }

        #endregion
    }
}
