namespace Mux.Core.Processes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Exposes a session's <see cref="BackgroundProcessRegistry"/> to the model as four tools: <c>process_start</c>
    /// and <c>process_stop</c> (mutating, so they take the approval path and the write lease) and
    /// <c>process_output</c> and <c>process_list</c> (read-only).
    /// </summary>
    public sealed class BackgroundProcessToolProvider : IExternalToolProvider
    {
        #region Private-Members

        private const int MaxReturnedChars = 60000;

        private readonly BackgroundProcessRegistry _Registry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BackgroundProcessToolProvider"/> class.
        /// </summary>
        /// <param name="registry">The session's process registry. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is null.</exception>
        public BackgroundProcessToolProvider(BackgroundProcessRegistry registry)
        {
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        #endregion

        #region Public-Members

        /// <summary>The tool that starts a process.</summary>
        public const string StartToolName = "process_start";

        /// <summary>The tool that reads a process's new output.</summary>
        public const string OutputToolName = "process_output";

        /// <summary>The tool that lists processes.</summary>
        public const string ListToolName = "process_list";

        /// <summary>The tool that stops a process.</summary>
        public const string StopToolName = "process_stop";

        /// <inheritdoc/>
        public string Name => "processes";

        /// <summary>The registry behind the tools.</summary>
        public BackgroundProcessRegistry Registry => _Registry;

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = StartToolName,
                    Description = "Start a long-running command in the background (a dev server, docker compose up, a watch-mode test runner) and return its id at once. It runs through the platform shell. Use run_process instead for commands that finish. Optionally wait for a line that shows it is ready.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            command = new { type = "string", description = "The command line to run." },
                            working_directory = new { type = "string", description = "Working directory. Defaults to the agent's working directory." },
                            name = new { type = "string", description = "Optional short label, for example 'web' or 'api'." },
                            wait_for = new { type = "string", description = "Optional regular expression; wait until the output matches it (for example 'ready|listening on|Local:')." },
                            timeout_ms = new { type = "integer", description = "How long to wait for wait_for, in milliseconds (default 30000, at most 300000)." }
                        },
                        required = new[] { "command" }
                    }
                },
                new ToolDefinition
                {
                    Name = OutputToolName,
                    Description = "Read the output a background process produced since the last read, with its running state and exit code. Optionally wait until the output matches a regular expression.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "string", description = "The process id from process_start, for example p1." },
                            wait_for = new { type = "string", description = "Optional regular expression to wait for in the new output." },
                            timeout_ms = new { type = "integer", description = "How long to wait, in milliseconds (default 0 without wait_for, 30000 with it; at most 300000)." }
                        },
                        required = new[] { "id" }
                    }
                },
                new ToolDefinition
                {
                    Name = ListToolName,
                    Description = "List this session's background processes with their ids, commands, state, and unread output.",
                    ParametersSchema = new { type = "object", properties = new { } }
                },
                new ToolDefinition
                {
                    Name = StopToolName,
                    Description = "Stop a background process and its child processes, and return its last unread output. Pass id 'all' to stop every one.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            id = new { type = "string", description = "The process id, or 'all'." }
                        },
                        required = new[] { "id" }
                    }
                }
            };
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            return IsTool(toolName, StartToolName) || IsTool(toolName, OutputToolName) || IsTool(toolName, ListToolName) || IsTool(toolName, StopToolName);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            return IsTool(toolName, OutputToolName) || IsTool(toolName, ListToolName) ? ToolMutationKind.ReadOnly : ToolMutationKind.Mutating;
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (IsTool(toolName, StartToolName)) return await StartAsync(toolName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
                if (IsTool(toolName, OutputToolName)) return await OutputAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
                if (IsTool(toolName, ListToolName)) return Ok(toolName, new { processes = new List<BackgroundProcessInfo>(_Registry.List()).ConvertAll(Describe) });
                if (IsTool(toolName, StopToolName)) return await StopAsync(toolName, arguments, cancellationToken).ConfigureAwait(false);
                return Error(toolName, "unknown_tool", "'" + toolName + "' is not a process tool.");
            }
            catch (KeyNotFoundException ex)
            {
                return Error(toolName, "unknown_process", ex.Message + " Use process_list to see the ids.");
            }
            catch (ArgumentException ex)
            {
                return Error(toolName, "invalid_arguments", ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Error(toolName, "process_limit", ex.Message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Error(toolName, "process_error", ex.Message);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ToolResult> StartAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            string command = ReadString(arguments, "command") ?? throw new ArgumentException("command is required.");
            string directory = ReadString(arguments, "working_directory") ?? workingDirectory;
            if (!Path.IsPathRooted(directory))
            {
                directory = Path.GetFullPath(Path.Combine(workingDirectory, directory));
            }

            string? waitFor = ReadString(arguments, "wait_for");
            int timeout = ReadInt(arguments, "timeout_ms") ?? 30000;
            BackgroundProcessInfo started = _Registry.Start(command, directory, ReadString(arguments, "name"));
            if (string.IsNullOrEmpty(waitFor))
            {
                return Ok(toolName, new
                {
                    id = started.Id,
                    pid = started.ProcessId,
                    command = started.Command,
                    working_directory = started.WorkingDirectory,
                    running = true,
                    next = "Read its output with process_output (id " + started.Id + "); stop it with process_stop."
                });
            }

            BackgroundProcessOutput output = await _Registry.ReadAsync(started.Id, waitFor, timeout, cancellationToken).ConfigureAwait(false);
            return Ok(toolName, new
            {
                id = started.Id,
                pid = started.ProcessId,
                command = started.Command,
                working_directory = started.WorkingDirectory,
                running = output.Running,
                exit_code = output.ExitCode,
                ready = output.Matched == true,
                match = output.MatchText,
                timed_out = output.TimedOut,
                output = Clip(output.Text),
                dropped_chars = output.DroppedChars
            }, success: output.Running || output.ExitCode == 0);
        }

        private async Task<ToolResult> OutputAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            string id = ReadString(arguments, "id") ?? throw new ArgumentException("id is required.");
            string? waitFor = ReadString(arguments, "wait_for");
            int timeout = ReadInt(arguments, "timeout_ms") ?? (string.IsNullOrEmpty(waitFor) ? 0 : 30000);
            BackgroundProcessOutput output = await _Registry.ReadAsync(id, waitFor, timeout, cancellationToken).ConfigureAwait(false);
            return Ok(toolName, new
            {
                id = output.Id,
                running = output.Running,
                exit_code = output.ExitCode,
                matched = output.Matched,
                match = output.MatchText,
                timed_out = output.TimedOut,
                output = Clip(output.Text),
                dropped_chars = output.DroppedChars
            });
        }

        private async Task<ToolResult> StopAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            string id = ReadString(arguments, "id") ?? throw new ArgumentException("id is required.");
            if (string.Equals(id.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                int stopped = _Registry.StopAll();
                return Ok(toolName, new { stopped });
            }

            BackgroundProcessInfo? before = _Registry.Get(id);
            BackgroundProcessOutput output = await _Registry.StopAsync(id, cancellationToken).ConfigureAwait(false);
            return Ok(toolName, new
            {
                id = output.Id,
                was_running = before?.Running ?? false,
                running = output.Running,
                exit_code = output.ExitCode,
                output = Clip(output.Text),
                dropped_chars = output.DroppedChars
            });
        }

        private static object Describe(BackgroundProcessInfo info)
        {
            return new
            {
                id = info.Id,
                name = info.Name,
                command = info.Command,
                working_directory = info.WorkingDirectory,
                pid = info.ProcessId,
                running = info.Running,
                exit_code = info.ExitCode,
                stopped_by_user = info.StoppedByUser,
                started_utc = info.StartedUtc.ToString("o", CultureInfo.InvariantCulture),
                unread_chars = info.UnreadChars
            };
        }

        private static string Clip(string text)
        {
            if (text.Length <= MaxReturnedChars)
            {
                return text;
            }

            return "[mux: showing the last " + MaxReturnedChars + " of " + text.Length + " characters]\n" + text.Substring(text.Length - MaxReturnedChars);
        }

        private static bool IsTool(string toolName, string expected)
        {
            return string.Equals(toolName, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadString(JsonElement arguments, string name)
        {
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            return null;
        }

        private static int? ReadInt(JsonElement arguments, string name)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
            if (value.ValueKind == JsonValueKind.Null) return null;
            throw new ArgumentException(name + " must be a whole number of milliseconds.");
        }

        private static ToolResult Ok(string toolName, object payload, bool success = true)
        {
            return new ToolResult { ToolCallId = toolName, Success = success, Content = JsonSerializer.Serialize(payload) };
        }

        private static ToolResult Error(string toolName, string code, string message)
        {
            return new ToolResult { ToolCallId = toolName, Success = false, Content = JsonSerializer.Serialize(new { error = code, message }) };
        }

        #endregion
    }
}
