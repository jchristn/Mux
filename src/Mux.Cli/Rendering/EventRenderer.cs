namespace Mux.Cli.Rendering
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Spectre.Console;

    /// <summary>
    /// Renders agent events to the terminal.
    /// </summary>
    public static class EventRenderer
    {
        #region Private-Members

        private static readonly int _MaxResultPreview = 200;
        private static ThinkingAnimation? _ActiveAnimation = null;

        // ANSI: dark grey background (236) + light grey text (250)
        private static readonly string _TextStyleOn = "\x1b[38;5;250m\x1b[48;5;236m";
        private static readonly string _TextStyleOff = "\x1b[0m";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Asynchronously renders a stream of agent events to the console.
        /// </summary>
        /// <param name="events">The async stream of agent events to render.</param>
        /// <param name="verbose">Whether to render verbose diagnostic output.</param>
        /// <returns>A task representing the async rendering operation.</returns>
        public static async Task RenderAsync(IAsyncEnumerable<AgentEvent> events, bool verbose)
        {
            bool wasStreaming = false;
            bool wasToolCall = false;
            bool waitingForFirstEvent = true;

            ThinkingAnimation animation = new ThinkingAnimation();
            _ActiveAnimation = animation;
            animation.Start();

            try
            {
                await foreach (AgentEvent agentEvent in events)
                {
                    if (waitingForFirstEvent)
                    {
                        animation.Stop();
                        _ActiveAnimation = null;
                        waitingForFirstEvent = false;
                    }

                    bool isTextEvent = agentEvent is AssistantTextEvent;

                    if (wasStreaming && !isTextEvent)
                    {
                        Console.Write(_TextStyleOff);
                        Console.WriteLine();
                        wasStreaming = false;
                    }

                    if (wasToolCall && isTextEvent)
                    {
                        Console.WriteLine();
                        wasToolCall = false;
                    }

                    switch (agentEvent)
                    {
                        case AssistantTextEvent textEvent:
                            if (!wasStreaming)
                            {
                                Console.Write(_TextStyleOn);
                            }
                            Console.Write(textEvent.Text);
                            wasStreaming = true;
                            wasToolCall = false;
                            break;

                        case ToolCallProposedEvent proposedEvent:
                            wasToolCall = true;
                            break;

                        case ToolCallApprovedEvent approvedEvent:
                            wasToolCall = true;
                            break;

                        case ToolCallCompletedEvent completedEvent:
                            RenderToolResult(completedEvent);
                            wasToolCall = true;
                            break;

                        case ErrorEvent errorEvent:
                            RenderError(errorEvent);
                            wasToolCall = false;
                            break;

                        case HeartbeatEvent heartbeatEvent:
                            if (verbose)
                            {
                                Console.Error.WriteLine($"  [step {heartbeatEvent.StepNumber}]");
                            }
                            break;

                        default:
                            break;
                    }
                }
            }
            finally
            {
                if (waitingForFirstEvent)
                {
                    animation.Stop();
                    _ActiveAnimation = null;
                }

                animation.Dispose();
            }

            if (wasStreaming)
            {
                Console.Write(_TextStyleOff);
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Updates the thinking/status line in-place for retry progress.
        /// </summary>
        /// <param name="message">The status message to display.</param>
        public static void UpdateStatusLine(string message)
        {
            ThinkingAnimation? animation = _ActiveAnimation;
            if (animation != null)
            {
                animation.ShowStatus(message);
            }
            else
            {
                Console.Write("\r");
                Console.Write(new string(' ', 80));
                Console.Write("\r");
                Console.Write($"\x1b[90m{message}\x1b[0m");
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Renders a completed tool call as a single line:
        /// [tool:name]: summary success/fail Nms
        /// </summary>
        /// <param name="completedEvent">The tool call completed event.</param>
        private static void RenderToolResult(ToolCallCompletedEvent completedEvent)
        {
            string name = completedEvent.ToolName;
            string summary = SummarizeResult(completedEvent.Result.Content);
            string status = completedEvent.Result.Success ? "ok" : "FAIL";
            long elapsed = completedEvent.ElapsedMs;

            if (completedEvent.Result.Success)
            {
                AnsiConsole.MarkupLine(
                    $"[dim][[tool:{Markup.Escape(name)}]][/] [dim]{Markup.Escape(summary)}[/] [green]{status}[/] [dim]{elapsed}ms[/]");
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"[dim][[tool:{Markup.Escape(name)}]][/] [red]{Markup.Escape(summary)}[/] [red]{status}[/] [dim]{elapsed}ms[/]");
            }
        }

        /// <summary>
        /// Renders an error event.
        /// </summary>
        /// <param name="errorEvent">The error event.</param>
        private static void RenderError(ErrorEvent errorEvent)
        {
            AnsiConsole.MarkupLine($"[red][[error]] {Markup.Escape(errorEvent.Code)}: {Markup.Escape(errorEvent.Message)}[/]");
        }

        /// <summary>
        /// Summarizes a tool result for compact display.
        /// </summary>
        private static string SummarizeResult(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return "(empty)";
            }

            try
            {
                JsonDocument doc = JsonDocument.Parse(content);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("success", out JsonElement successEl) && successEl.GetBoolean())
                {
                    if (root.TryGetProperty("file_path", out JsonElement pathEl))
                    {
                        string path = pathEl.GetString() ?? "";
                        string fileName = System.IO.Path.GetFileName(path);
                        if (root.TryGetProperty("line_count", out JsonElement lcEl))
                        {
                            return $"{fileName} ({lcEl.GetInt32()} lines)";
                        }
                        if (root.TryGetProperty("edits_applied", out JsonElement eaEl))
                        {
                            return $"{fileName} ({eaEl.GetInt32()} edits)";
                        }
                        return fileName;
                    }
                    if (root.TryGetProperty("path", out JsonElement dirPathEl))
                    {
                        return System.IO.Path.GetFileName(dirPathEl.GetString() ?? "") + "/";
                    }
                }

                if (root.TryGetProperty("success", out JsonElement failEl) && !failEl.GetBoolean())
                {
                    if (root.TryGetProperty("error", out JsonElement errEl))
                    {
                        return errEl.GetString() ?? "error";
                    }
                    if (root.TryGetProperty("message", out JsonElement msgEl))
                    {
                        return TruncateString(msgEl.GetString() ?? "", _MaxResultPreview);
                    }
                }
            }
            catch
            {
                // Not JSON
            }

            int lineCount = 1;
            for (int i = 0; i < content.Length; i++)
            {
                if (content[i] == '\n')
                {
                    lineCount++;
                }
            }

            if (lineCount > 10)
            {
                return $"({lineCount} lines)";
            }

            return TruncateString(content.Replace("\r\n", " ").Replace("\n", " "), _MaxResultPreview);
        }

        /// <summary>
        /// Truncates a string to the specified maximum length.
        /// </summary>
        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength) + "...";
        }

        #endregion
    }
}
