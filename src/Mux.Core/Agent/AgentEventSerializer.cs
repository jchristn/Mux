namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Tasks;

    /// <summary>
    /// Converts <see cref="AgentEvent"/> instances into the canonical, machine-readable event envelope shared
    /// by every mux surface that streams run events: the CLI's headless JSONL output, the <c>mux serve</c>
    /// Server-Sent Events and WebSocket bridges. Producing one envelope from one place guarantees that a
    /// WebSocket subscriber and a headless JSONL consumer observe byte-identical payloads for the same event.
    ///
    /// <para>All string content is redacted for common secret shapes (bearer tokens, <c>sk-</c> keys,
    /// <c>authorization</c>/<c>api-key</c> assignments) before serialization. Output is compact
    /// (non-indented) camelCase JSON and carries a <see cref="ContractVersion"/> field so consumers can gate
    /// on the schema version.</para>
    ///
    /// <para>Thread safety: the type is stateless and its members are safe to call concurrently.</para>
    /// </summary>
    public static class AgentEventSerializer
    {
        /// <summary>
        /// The structured-output contract version stamped on every serialized envelope. Consumers should
        /// treat an unrecognized higher value as potentially additive and ignore unknown fields.
        /// </summary>
        public const int ContractVersion = 2;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Serializes an <see cref="AgentEvent"/> into a single JSONL-safe line, including run statistics on
        /// the terminal <c>run_completed</c> event.
        /// </summary>
        /// <param name="agentEvent">The event to serialize. Must not be null.</param>
        /// <returns>A compact, single-line JSON string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentEvent"/> is null.</exception>
        public static string ToEnvelopeLine(AgentEvent agentEvent)
        {
            return ToEnvelopeLine(agentEvent, includeStats: true);
        }

        /// <summary>
        /// Serializes an <see cref="AgentEvent"/> into a single JSONL-safe line. When
        /// <paramref name="includeStats"/> is false, the terminal <c>run_completed</c> event omits its
        /// run-metrics block and the <c>usage</c> object, keeping only identity and status fields.
        /// </summary>
        /// <param name="agentEvent">The event to serialize. Must not be null.</param>
        /// <param name="includeStats">Whether to include run metrics and token usage on the run summary event.</param>
        /// <returns>A compact, single-line JSON string.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentEvent"/> is null.</exception>
        public static string ToEnvelopeLine(AgentEvent agentEvent, bool includeStats)
        {
            return JsonSerializer.Serialize(ToEnvelope(agentEvent, includeStats), _JsonOptions);
        }

        /// <summary>
        /// Builds the envelope payload dictionary for an <see cref="AgentEvent"/> without serializing it, so a
        /// transport (for example a WebSocket frame) can embed or re-shape it without re-parsing a JSON string.
        /// </summary>
        /// <param name="agentEvent">The event to project. Must not be null.</param>
        /// <param name="includeStats">Whether to include run metrics and token usage on the run summary event.</param>
        /// <returns>An ordered dictionary of the envelope fields.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="agentEvent"/> is null.</exception>
        public static Dictionary<string, object?> ToEnvelope(AgentEvent agentEvent, bool includeStats)
        {
            if (agentEvent == null) throw new ArgumentNullException(nameof(agentEvent));

            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                ["contractVersion"] = ContractVersion,
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
                    payload["text"] = Redact(textEvent.Text);
                    break;

                case AssistantThinkingEvent thinkingEvent:
                    payload["text"] = Redact(thinkingEvent.Text);
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
                    payload["message"] = Redact(errorEvent.Message);
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
                    if (includeStats)
                    {
                        payload["iterationsCompleted"] = runCompletedEvent.IterationsCompleted;
                        payload["toolCallCount"] = runCompletedEvent.ToolCallCount;
                        payload["errorCount"] = runCompletedEvent.ErrorCount;
                        payload["assistantTextChars"] = runCompletedEvent.AssistantTextChars;
                        payload["durationMs"] = runCompletedEvent.DurationMs;
                        payload["finalEstimatedTokens"] = runCompletedEvent.FinalEstimatedTokens;
                        payload["compactionCount"] = runCompletedEvent.CompactionCount;
                        payload["usage"] = FormatUsage(runCompletedEvent);
                    }
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

            return payload;
        }

        /// <summary>
        /// Redacts common secret shapes (bearer tokens, <c>sk-</c> keys, and <c>authorization</c>/<c>api-key</c>
        /// assignments) from an arbitrary string. Null is treated as an empty string. Shared with the CLI's
        /// run-summary formatting so redaction is defined in one place.
        /// </summary>
        /// <param name="value">The value to redact. May be null.</param>
        /// <returns>The redacted string, never null.</returns>
        public static string Redact(string? value)
        {
            string result = value ?? string.Empty;
            result = Regex.Replace(result, @"Bearer\s+[A-Za-z0-9_\-\.=]+", "Bearer ***REDACTED***", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\bsk-[A-Za-z0-9]+\b", "***REDACTED***", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\b(x-api-key|api-key|authorization)\s*[:=]\s*[^\s,;]+", "$1=***REDACTED***", RegexOptions.IgnoreCase);
            return result;
        }

        /// <summary>
        /// Builds the token-usage sub-object for a run summary. Zero-fills when the event is null.
        /// </summary>
        /// <param name="completed">The terminal run-completed event, or null.</param>
        /// <returns>A dictionary of input/output/total/estimated token counts.</returns>
        public static Dictionary<string, object?> FormatUsage(RunCompletedEvent? completed)
        {
            return new Dictionary<string, object?>
            {
                ["inputTokens"] = completed?.InputTokens ?? 0,
                ["outputTokens"] = completed?.OutputTokens ?? 0,
                ["totalTokens"] = completed?.TotalTokens ?? 0,
                ["estimatedTokens"] = completed?.FinalEstimatedTokens ?? 0
            };
        }

        /// <summary>
        /// Projects a task-plan summary into its serialized shape.
        /// </summary>
        /// <param name="summary">The task-plan summary. Must not be null.</param>
        /// <returns>A dictionary of per-status task counts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary"/> is null.</exception>
        public static object FormatTaskSummary(TaskPlanSummary summary)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

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
                ["title"] = Redact(task.Title),
                ["status"] = GetTaskStatusName(task.Status),
                ["dependsOn"] = task.DependsOn,
                ["note"] = task.Note == null ? null : Redact(task.Note),
                ["durationMs"] = task.DurationMs,
                ["failureMessage"] = task.FailureMessage == null ? null : Redact(task.FailureMessage)
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
                ["arguments"] = ParseAndRedactJson(toolCall.Arguments) ?? Redact(toolCall.Arguments)
            };
        }

        private static object FormatToolResult(ToolResult toolResult)
        {
            return new Dictionary<string, object?>
            {
                ["toolCallId"] = toolResult.ToolCallId,
                ["success"] = toolResult.Success,
                ["content"] = ParseAndRedactJson(toolResult.Content) ?? Redact(toolResult.Content)
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
            catch (JsonException)
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
    }
}
