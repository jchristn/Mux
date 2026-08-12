namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tasks;

    /// <summary>
    /// Converts mux runtime objects into machine-readable structured output.
    /// </summary>
    public static class StructuredOutputFormatter
    {
        #region Private-Members

        private const int StructuredOutputContractVersion = 1;

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
                ["contractVersion"] = StructuredOutputContractVersion,
                ["eventType"] = GetEventTypeName(agentEvent.EventType),
                ["timestampUtc"] = agentEvent.TimestampUtc
            };

            switch (agentEvent)
            {
                case RunStartedEvent startedEvent:
                    payload["runId"] = startedEvent.RunId;
                    payload["sessionId"] = startedEvent.SessionId;
                    payload["endpointName"] = startedEvent.EndpointName;
                    payload["adapterType"] = startedEvent.AdapterType;
                    payload["baseUrl"] = startedEvent.BaseUrl;
                    payload["model"] = startedEvent.Model;
                    payload["commandName"] = startedEvent.CommandName;
                    payload["approvalPolicy"] = startedEvent.ApprovalPolicy;
                    payload["workingDirectory"] = startedEvent.WorkingDirectory;
                    payload["maxIterations"] = startedEvent.MaxIterations;
                    payload["toolsEnabled"] = startedEvent.ToolsEnabled;
                    payload["configDirectory"] = startedEvent.ConfigDirectory;
                    payload["endpointSelectionSource"] = startedEvent.EndpointSelectionSource;
                    payload["cliOverridesApplied"] = startedEvent.CliOverridesApplied;
                    payload["builtInToolCount"] = startedEvent.BuiltInToolCount;
                    payload["effectiveToolCount"] = startedEvent.EffectiveToolCount;
                    payload["contextWindow"] = startedEvent.ContextWindow;
                    payload["reservedOutputTokens"] = startedEvent.ReservedOutputTokens;
                    payload["usableInputLimit"] = startedEvent.UsableInputLimit;
                    payload["warningThresholdTokens"] = startedEvent.WarningThresholdTokens;
                    payload["tokenEstimationRatio"] = startedEvent.TokenEstimationRatio;
                    payload["compactionStrategy"] = startedEvent.CompactionStrategy;
                    payload["ignoreCertErrors"] = startedEvent.IgnoreCertErrors;
                    payload["sandboxPosture"] = startedEvent.SandboxPosture;
                    payload["reasoningEffort"] = FormatReasoningEffort(startedEvent.ReasoningEffort);
                    payload["showThinking"] = startedEvent.ShowThinking;
                    payload["mcp"] = new Dictionary<string, object?>
                    {
                        ["supported"] = startedEvent.McpSupported,
                        ["configured"] = startedEvent.McpConfigured,
                        ["serverCount"] = startedEvent.McpServerCount
                    };
                    break;

                case AssistantTextEvent textEvent:
                    payload["text"] = RedactString(textEvent.Text);
                    break;

                case AssistantThinkingEvent thinkingEvent:
                    payload["text"] = RedactString(thinkingEvent.Text);
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
                    payload["errorCode"] = errorEvent.Code;
                    payload["message"] = RedactString(errorEvent.Message);
                    AddIfNotEmpty(payload, "failureCategory", !string.IsNullOrWhiteSpace(errorEvent.FailureCategory)
                        ? errorEvent.FailureCategory
                        : ClassifyFailureCategory(errorEvent.Code));
                    AddIfNotEmpty(payload, "endpointName", errorEvent.EndpointName);
                    AddIfNotEmpty(payload, "adapterType", errorEvent.AdapterType);
                    AddIfNotEmpty(payload, "baseUrl", errorEvent.BaseUrl);
                    AddIfNotEmpty(payload, "model", errorEvent.Model);
                    AddIfNotEmpty(payload, "commandName", errorEvent.CommandName);
                    AddIfNotEmpty(payload, "configDirectory", errorEvent.ConfigDirectory);
                    AddIfNotEmpty(payload, "endpointSelectionSource", errorEvent.EndpointSelectionSource);
                    if (errorEvent.CliOverridesApplied.Count > 0)
                    {
                        payload["cliOverridesApplied"] = errorEvent.CliOverridesApplied;
                    }
                    break;

                case HeartbeatEvent heartbeatEvent:
                    payload["stepNumber"] = heartbeatEvent.StepNumber;
                    break;

                case ContextStatusEvent contextStatusEvent:
                    payload["scope"] = contextStatusEvent.Scope;
                    payload["estimatedTokens"] = contextStatusEvent.EstimatedTokens;
                    payload["usableInputLimit"] = contextStatusEvent.UsableInputLimit;
                    payload["remainingTokens"] = contextStatusEvent.RemainingTokens;
                    payload["remainingPercent"] = contextStatusEvent.RemainingPercent;
                    payload["warningThresholdTokens"] = contextStatusEvent.WarningThresholdTokens;
                    payload["messageCount"] = contextStatusEvent.MessageCount;
                    payload["trigger"] = contextStatusEvent.Trigger;
                    payload["warningLevel"] = contextStatusEvent.WarningLevel;
                    break;

                case ContextCompactedEvent contextCompactedEvent:
                    payload["scope"] = contextCompactedEvent.Scope;
                    payload["mode"] = contextCompactedEvent.Mode;
                    payload["strategy"] = contextCompactedEvent.Strategy;
                    payload["messagesBefore"] = contextCompactedEvent.MessagesBefore;
                    payload["messagesAfter"] = contextCompactedEvent.MessagesAfter;
                    payload["estimatedTokensBefore"] = contextCompactedEvent.EstimatedTokensBefore;
                    payload["estimatedTokensAfter"] = contextCompactedEvent.EstimatedTokensAfter;
                    payload["summaryCreated"] = contextCompactedEvent.SummaryCreated;
                    payload["reason"] = contextCompactedEvent.Reason;
                    break;

                case RunCompletedEvent runCompletedEvent:
                    payload["runId"] = runCompletedEvent.RunId;
                    payload["sessionId"] = runCompletedEvent.SessionId;
                    payload["status"] = runCompletedEvent.Status;
                    payload["iterationsCompleted"] = runCompletedEvent.IterationsCompleted;
                    payload["toolCallCount"] = runCompletedEvent.ToolCallCount;
                    payload["errorCount"] = runCompletedEvent.ErrorCount;
                    payload["assistantTextChars"] = runCompletedEvent.AssistantTextChars;
                    payload["durationMs"] = runCompletedEvent.DurationMs;
                    payload["finalEstimatedTokens"] = runCompletedEvent.FinalEstimatedTokens;
                    payload["compactionCount"] = runCompletedEvent.CompactionCount;
                    if (runCompletedEvent.TaskSummary != null)
                    {
                        payload["taskSummary"] = FormatTaskSummary(runCompletedEvent.TaskSummary);
                    }
                    break;

                case TaskPlanUpdatedEvent taskPlanEvent:
                    payload["changeKind"] = GetTaskChangeKindName(taskPlanEvent.ChangeKind);
                    payload["changedTaskId"] = taskPlanEvent.ChangedTaskId;
                    payload["totalCount"] = taskPlanEvent.TotalCount;
                    payload["completedCount"] = taskPlanEvent.CompletedCount;
                    List<object> taskItems = new List<object>();
                    foreach (AgentTask task in taskPlanEvent.Tasks)
                    {
                        taskItems.Add(FormatTask(task));
                    }
                    payload["tasks"] = taskItems;
                    break;
            }

            return JsonSerializer.Serialize(payload, _JsonOptions);
        }

        /// <summary>
        /// Serializes a single-object run summary for <c>print --output-format json</c>. Aggregates the
        /// terminal <see cref="RunCompletedEvent"/> with the accumulated final assistant text into one
        /// redacted JSON object carrying the contract version.
        /// </summary>
        /// <param name="completed">The terminal run-completed event, or null when the run produced none.</param>
        /// <param name="resultText">The accumulated final assistant response text. Null is treated as empty.</param>
        /// <param name="sessionId">The persisted session id, or null/empty when the run was not persisted.</param>
        /// <returns>A compact, single-line JSON object.</returns>
        public static string FormatRunSummary(RunCompletedEvent? completed, string? resultText, string? sessionId)
        {
            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                ["contractVersion"] = StructuredOutputContractVersion,
                ["result"] = RedactString(resultText),
                ["sessionId"] = string.IsNullOrEmpty(sessionId) ? string.Empty : sessionId,
                ["status"] = completed?.Status ?? "unknown",
                ["iterationsCompleted"] = completed?.IterationsCompleted ?? 0,
                ["toolCallCount"] = completed?.ToolCallCount ?? 0,
                ["errorCount"] = completed?.ErrorCount ?? 0,
                ["durationMs"] = completed?.DurationMs ?? 0,
                ["finalEstimatedTokens"] = completed?.FinalEstimatedTokens ?? 0,
                ["compactionCount"] = completed?.CompactionCount ?? 0
            };

            if (completed?.TaskSummary != null)
            {
                payload["taskSummary"] = FormatTaskSummary(completed.TaskSummary);
            }

            return JsonSerializer.Serialize(payload, _JsonOptions);
        }

        /// <summary>
        /// Serializes an arbitrary object as compact JSON.
        /// </summary>
        public static string FormatObject(object value)
        {
            Dictionary<string, object?> payload = ObjectToDictionary(value);
            payload["contractVersion"] = StructuredOutputContractVersion;
            return JsonSerializer.Serialize(payload, _JsonOptions);
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

        private static void AddIfNotEmpty(Dictionary<string, object?> payload, string propertyName, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                payload[propertyName] = value;
            }
        }

        private static object FormatTask(AgentTask task)
        {
            return new Dictionary<string, object?>
            {
                ["id"] = task.Id,
                ["title"] = RedactString(task.Title),
                ["status"] = GetTaskStatusName(task.Status),
                ["dependsOn"] = task.DependsOn,
                ["note"] = task.Note == null ? null : RedactString(task.Note),
                ["durationMs"] = task.DurationMs,
                ["failureMessage"] = task.FailureMessage == null ? null : RedactString(task.FailureMessage)
            };
        }

        private static object FormatTaskSummary(TaskPlanSummary summary)
        {
            return new Dictionary<string, object?>
            {
                ["total"] = summary.Total,
                ["completed"] = summary.Completed,
                ["pending"] = summary.Pending,
                ["inProgress"] = summary.InProgress,
                ["failed"] = summary.Failed,
                ["skipped"] = summary.Skipped,
                ["blocked"] = summary.Blocked
            };
        }

        private static string GetTaskStatusName(AgentTaskStatusEnum status)
        {
            return status switch
            {
                AgentTaskStatusEnum.Pending => "pending",
                AgentTaskStatusEnum.InProgress => "in_progress",
                AgentTaskStatusEnum.Completed => "completed",
                AgentTaskStatusEnum.Failed => "failed",
                AgentTaskStatusEnum.Skipped => "skipped",
                AgentTaskStatusEnum.Blocked => "blocked",
                _ => status.ToString()
            };
        }

        private static string GetTaskChangeKindName(TaskPlanChangeKindEnum changeKind)
        {
            return changeKind switch
            {
                TaskPlanChangeKindEnum.PlanCreated => "plan_created",
                TaskPlanChangeKindEnum.PlanReplaced => "plan_replaced",
                TaskPlanChangeKindEnum.TaskStatusChanged => "task_status_changed",
                TaskPlanChangeKindEnum.TaskNoteUpdated => "task_note_updated",
                TaskPlanChangeKindEnum.PlanCleared => "plan_cleared",
                _ => changeKind.ToString()
            };
        }

        private static Dictionary<string, object?>? FormatReasoningEffort(ReasoningEffortConfig? config)
        {
            if (config == null || !config.Level.HasValue)
            {
                return null;
            }

            Dictionary<string, object?> result = new Dictionary<string, object?>
            {
                ["level"] = ReasoningLevelEnumConverter.ToWire(config.Level.Value)
            };

            if (!string.IsNullOrWhiteSpace(config.OpenAiValue)) result["openAiValue"] = config.OpenAiValue;
            if (config.GeminiThinkingBudget.HasValue) result["geminiThinkingBudget"] = config.GeminiThinkingBudget.Value;
            if (!string.IsNullOrWhiteSpace(config.OllamaThink)) result["ollamaThink"] = config.OllamaThink;
            return result;
        }

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
                Dictionary<string, object?>? result =
                    JsonSerializer.Deserialize<Dictionary<string, object?>>(value, _JsonOptions);
                if (result == null)
                {
                    return null;
                }

                RedactDictionary(result);
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static void RedactDictionary(Dictionary<string, object?> dictionary)
        {
            List<string> keys = new List<string>(dictionary.Keys);
            foreach (string key in keys)
            {
                if (IsSensitiveKey(key))
                {
                    dictionary[key] = "***REDACTED***";
                }
            }
        }

        private static Dictionary<string, object?> ObjectToDictionary(object value)
        {
            Dictionary<string, object?> result = new Dictionary<string, object?>();
            if (value is IDictionary<string, object?> dictionary)
            {
                foreach (KeyValuePair<string, object?> pair in dictionary)
                {
                    result[pair.Key] = pair.Value;
                }

                return result;
            }

            foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead)
                {
                    continue;
                }

                string propertyName = _JsonOptions.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                result[propertyName] = property.GetValue(value);
            }

            return result;
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
                AgentEventTypeEnum.AssistantThinking => "assistant_thinking",
                AgentEventTypeEnum.ToolCallProposed => "tool_call_proposed",
                AgentEventTypeEnum.ToolCallApproved => "tool_call_approved",
                AgentEventTypeEnum.ToolCallCompleted => "tool_call_completed",
                AgentEventTypeEnum.Error => "error",
                AgentEventTypeEnum.Heartbeat => "heartbeat",
                AgentEventTypeEnum.ContextStatus => "context_status",
                AgentEventTypeEnum.ContextCompacted => "context_compacted",
                AgentEventTypeEnum.RunCompleted => "run_completed",
                AgentEventTypeEnum.TaskPlanUpdated => "task_plan_updated",
                _ => eventType.ToString()
            };
        }

        private static string ClassifyFailureCategory(string code)
        {
            return code switch
            {
                "endpoint_not_found" => "configuration",
                "unsupported_option" => "configuration",
                "invalid_argument" => "configuration",
                "config_error" => "configuration",
                "cancelled" => "cancellation",
                "tool_call_denied" => "approval",
                "approval_error" => "approval",
                "tool_execution_error" => "tool",
                "llm_connection_error" => "network",
                "llm_error" => "backend",
                "llm_stream_error" => "backend",
                "context_limit_exceeded" => "runtime",
                "max_iterations_reached" => "runtime",
                "budget_exceeded" => "runtime",
                "schema_validation_failed" => "validation",
                "print_error" => "unknown",
                _ => string.Empty
            };
        }

        #endregion
    }
}
