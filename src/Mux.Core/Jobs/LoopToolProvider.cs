namespace Mux.Core.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Exposes the <c>schedule_next</c> tool, through which the model paces a self-paced loop: run again after a
    /// delay, or stop. The tool is offered only while a self-paced loop iteration is running, so ordinary turns
    /// never see it.
    /// </summary>
    public sealed class LoopToolProvider : IExternalToolProvider
    {
        #region Private-Members

        private readonly LoopScheduler _Scheduler;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="LoopToolProvider"/> class.
        /// </summary>
        /// <param name="scheduler">The session's loop scheduler. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="scheduler"/> is null.</exception>
        public LoopToolProvider(LoopScheduler scheduler)
        {
            _Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        #endregion

        #region Public-Members

        /// <summary>The tool name.</summary>
        public const string ScheduleNextToolName = "schedule_next";

        /// <inheritdoc/>
        public string Name => "loops";

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            List<ToolDefinition> definitions = new List<ToolDefinition>();
            bool selfPacedRunning = false;
            foreach (LoopDefinition loop in _Scheduler.List())
            {
                if (loop.Status == LoopStatusEnum.Running && loop.IsSelfPaced)
                {
                    selfPacedRunning = true;
                    break;
                }
            }

            if (!selfPacedRunning)
            {
                return definitions;
            }

            definitions.Add(new ToolDefinition
            {
                Name = ScheduleNextToolName,
                Description = "Pace the self-paced mux loop that is running: schedule its next iteration after delay_seconds, or stop it with stop: true. Call it once, at the end of the iteration. If it is not called, the loop stops.",
                ParametersSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        delay_seconds = new { type = "integer", description = "Seconds to wait before the next iteration (" + _Scheduler.MinIntervalSeconds.ToString(CultureInfo.InvariantCulture) + " to " + LoopScheduler.MaxDelaySeconds.ToString(CultureInfo.InvariantCulture) + "). Required unless stop is true." },
                        stop = new { type = "boolean", description = "True to end the loop because the goal is met or nothing more can be done." },
                        reason = new { type = "string", description = "One short sentence explaining the choice." },
                        loop_id = new { type = "string", description = "The loop id from the iteration header, for example L1. Optional when only one loop is running." }
                    },
                    required = new[] { "reason" }
                }
            });
            return definitions;
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            return string.Equals(toolName, ScheduleNextToolName, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            return ToolMutationKind.ReadOnly;
        }

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (!HasTool(toolName))
            {
                return Task.FromResult(Result(toolName, false, new { error = "unknown_tool", message = "'" + toolName + "' is not a loop tool." }));
            }

            string? loopId = ReadString(arguments, "loop_id");
            string? reason = ReadString(arguments, "reason");
            bool stop = ReadBool(arguments, "stop");
            int? delay = ReadInt(arguments, "delay_seconds", out bool delayInvalid);
            if (delayInvalid)
            {
                return Task.FromResult(Result(toolName, false, new { error = "invalid_arguments", message = "delay_seconds must be a whole number of seconds." }));
            }

            if (_Scheduler.TryRecordDecision(loopId, delay, stop, reason, out string message))
            {
                return Task.FromResult(Result(toolName, true, new { scheduled = !stop, stop, delay_seconds = stop ? (int?)null : delay, message }));
            }

            return Task.FromResult(Result(toolName, false, new { error = "not_scheduled", message }));
        }

        #endregion

        #region Private-Methods

        private static ToolResult Result(string toolCallId, bool success, object payload)
        {
            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = success,
                Content = JsonSerializer.Serialize(payload)
            };
        }

        private static string? ReadString(JsonElement arguments, string name)
        {
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            return null;
        }

        private static bool ReadBool(JsonElement arguments, string name)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out JsonElement value))
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.String) return string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static int? ReadInt(JsonElement arguments, string name, out bool invalid)
        {
            invalid = false;
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double real) && real == Math.Floor(real) && real >= int.MinValue && real <= int.MaxValue)
            {
                return (int)real;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            invalid = true;
            return null;
        }

        #endregion
    }
}
