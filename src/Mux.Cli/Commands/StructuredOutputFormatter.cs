namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Text.Json;
    using Mux.Core.Agent;

    /// <summary>
    /// Converts mux runtime objects into machine-readable structured output for the CLI's headless modes.
    /// Event serialization is delegated to <see cref="AgentEventSerializer"/> in Mux.Core so the CLI's JSONL
    /// output and the <c>mux serve</c> streaming bridges share one contract; this type keeps the CLI-only
    /// run-summary, stats-footer, and arbitrary-object formatting.
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
        /// Serializes an <see cref="AgentEvent"/> into a JSONL-safe line, including run statistics.
        /// </summary>
        public static string FormatEvent(AgentEvent agentEvent)
        {
            return AgentEventSerializer.ToEnvelopeLine(agentEvent, includeStats: true);
        }

        /// <summary>
        /// Serializes an <see cref="AgentEvent"/> into a JSONL-safe line. When <paramref name="includeStats"/>
        /// is false, the terminal <c>run_completed</c> event omits its run-metrics block and the
        /// <c>usage</c> object, keeping only identity and status fields.
        /// </summary>
        /// <param name="agentEvent">The event to serialize.</param>
        /// <param name="includeStats">Whether to include run metrics and token usage on the run summary event.</param>
        public static string FormatEvent(AgentEvent agentEvent, bool includeStats)
        {
            return AgentEventSerializer.ToEnvelopeLine(agentEvent, includeStats);
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
            return FormatRunSummary(completed, resultText, sessionId, includeStats: true);
        }

        /// <summary>
        /// Serializes a single-object run summary for <c>print --output-format json</c>. When
        /// <paramref name="includeStats"/> is false, the run-metrics block and the <c>usage</c> object are
        /// omitted, leaving only <c>contractVersion</c>, <c>result</c>, <c>sessionId</c>, and <c>status</c>
        /// (plus <c>taskSummary</c> when the run had a task plan).
        /// </summary>
        /// <param name="completed">The terminal run-completed event, or null when the run produced none.</param>
        /// <param name="resultText">The accumulated final assistant response text. Null is treated as empty.</param>
        /// <param name="sessionId">The persisted session id, or null/empty when the run was not persisted.</param>
        /// <param name="includeStats">Whether to include run metrics and token usage.</param>
        /// <returns>A compact, single-line JSON object.</returns>
        public static string FormatRunSummary(RunCompletedEvent? completed, string? resultText, string? sessionId, bool includeStats)
        {
            Dictionary<string, object?> payload = new Dictionary<string, object?>
            {
                ["contractVersion"] = AgentEventSerializer.ContractVersion,
                ["result"] = AgentEventSerializer.Redact(resultText),
                ["sessionId"] = string.IsNullOrEmpty(sessionId) ? string.Empty : sessionId,
                ["status"] = completed?.Status ?? "unknown"
            };

            if (includeStats)
            {
                payload["iterationsCompleted"] = completed?.IterationsCompleted ?? 0;
                payload["toolCallCount"] = completed?.ToolCallCount ?? 0;
                payload["errorCount"] = completed?.ErrorCount ?? 0;
                payload["durationMs"] = completed?.DurationMs ?? 0;
                payload["finalEstimatedTokens"] = completed?.FinalEstimatedTokens ?? 0;
                payload["compactionCount"] = completed?.CompactionCount ?? 0;
                payload["usage"] = AgentEventSerializer.FormatUsage(completed);
            }

            if (completed?.TaskSummary != null)
            {
                payload["taskSummary"] = AgentEventSerializer.FormatTaskSummary(completed.TaskSummary);
            }

            return JsonSerializer.Serialize(payload, _JsonOptions);
        }

        /// <summary>
        /// Formats the human-readable, single-line token/statistics footer written to stderr after a
        /// <c>--output-format text</c> run when <c>--stats</c> is requested. Never touches stdout, so the
        /// answer stream stays clean for pipes.
        /// </summary>
        /// <param name="completed">The terminal run-completed event, or null when the run produced none.</param>
        /// <returns>A one-line summary suitable for stderr.</returns>
        public static string FormatTextStatsFooter(RunCompletedEvent? completed)
        {
            if (completed == null)
            {
                return "mux: no run statistics available.";
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "mux: tokens input={0} output={1} total={2} (est {3}) | duration={4}ms | turns={5} | tools={6} | errors={7}",
                completed.InputTokens,
                completed.OutputTokens,
                completed.TotalTokens,
                completed.FinalEstimatedTokens,
                completed.DurationMs,
                completed.IterationsCompleted,
                completed.ToolCallCount,
                completed.ErrorCount);
        }

        /// <summary>
        /// Serializes an arbitrary object as compact JSON.
        /// </summary>
        public static string FormatObject(object value)
        {
            Dictionary<string, object?> payload = ObjectToDictionary(value);
            payload["contractVersion"] = AgentEventSerializer.ContractVersion;
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

        #endregion
    }
}
