namespace Mux.Cli.Rendering
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Utility;
    using Spectre.Console;

    /// <summary>
    /// Renders agent events to the terminal.
    /// </summary>
    public static class EventRenderer
    {
        #region Private-Members

        private static readonly int _MaxResultPreview = 200;
        private static ThinkingAnimation? _ActiveAnimation = null;

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
                    if (waitingForFirstEvent && ShouldStopThinkingAnimation(agentEvent))
                    {
                        animation.Stop();
                        _ActiveAnimation = null;
                        waitingForFirstEvent = false;
                    }

                    bool isTextEvent = agentEvent is AssistantTextEvent;

                    if (wasStreaming && !isTextEvent)
                    {
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
                                Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Step {heartbeatEvent.StepNumber}"));
                            }
                            break;

                        case ContextStatusEvent contextStatusEvent:
                            RenderContextStatus(contextStatusEvent);
                            wasToolCall = false;
                            break;

                        case ContextCompactedEvent contextCompactedEvent:
                            RenderContextCompacted(contextCompactedEvent);
                            wasToolCall = false;
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
        /// Renders a completed tool call as a single line.
        /// </summary>
        /// <param name="completedEvent">The tool call completed event.</param>
        private static void RenderToolResult(ToolCallCompletedEvent completedEvent)
        {
            string name = completedEvent.ToolName;
            string summary = SummarizeResult(completedEvent.Result.Content);
            long elapsed = completedEvent.ElapsedMs;

            if (completedEvent.Result.Success)
            {
                string line = ToolCallRenderer.FormatToolExecutionLine(name, summary, "ok", elapsed);
                AnsiConsole.MarkupLine(
                    $"[green]{Markup.Escape(line)}[/]");
            }
            else
            {
                string line = ToolCallRenderer.FormatToolExecutionLine(name, summary, "failed", elapsed);
                AnsiConsole.MarkupLine(
                    $"[red]{Markup.Escape(line)}[/]");
            }
        }

        /// <summary>
        /// Renders an error event.
        /// </summary>
        /// <param name="errorEvent">The error event.</param>
        private static void RenderError(ErrorEvent errorEvent)
        {
            AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(errorEvent.Code)}: {Markup.Escape(errorEvent.Message)}[/]");
        }

        /// <summary>
        /// Renders a context usage event.
        /// </summary>
        /// <param name="contextStatusEvent">The context status event.</param>
        private static void RenderContextStatus(ContextStatusEvent contextStatusEvent)
        {
            string line =
                $"Context usage: est. {contextStatusEvent.EstimatedTokens} / {contextStatusEvent.UsableInputLimit} tokens | {contextStatusEvent.RemainingTokens} remaining.";

            if (string.Equals(contextStatusEvent.WarningLevel, "critical", StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine($"[red]{Markup.Escape(line)}[/]");
            }
            else if (string.Equals(contextStatusEvent.WarningLevel, "approaching", StringComparison.Ordinal))
            {
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(line)}[/]");
            }
        }

        /// <summary>
        /// Renders a context compaction event.
        /// </summary>
        /// <param name="contextCompactedEvent">The compaction event.</param>
        private static void RenderContextCompacted(ContextCompactedEvent contextCompactedEvent)
        {
            string line =
                $"Auto-compacted context ({contextCompactedEvent.Strategy}): est. {contextCompactedEvent.EstimatedTokensBefore} -> {contextCompactedEvent.EstimatedTokensAfter} tokens.";
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(line)}[/]");
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
                ToolResultSummary? root = JsonSerializer.Deserialize<ToolResultSummary>(content);

                if (root?.Success == true)
                {
                    if (!string.IsNullOrWhiteSpace(root.FilePath))
                    {
                        string path = root.FilePath;
                        string fileName = System.IO.Path.GetFileName(path);
                        if (root.LineCount.HasValue)
                        {
                            return $"{fileName} ({root.LineCount.Value} lines)";
                        }
                        if (root.EditsApplied.HasValue)
                        {
                            return $"{fileName} ({root.EditsApplied.Value} edits)";
                        }
                        return fileName;
                    }
                    if (!string.IsNullOrWhiteSpace(root.Path))
                    {
                        return System.IO.Path.GetFileName(root.Path) + "/";
                    }
                }

                if (root?.Success == false)
                {
                    if (!string.IsNullOrWhiteSpace(root.Error))
                    {
                        return root.Error;
                    }
                    if (!string.IsNullOrWhiteSpace(root.Message))
                    {
                        return TruncateString(root.Message, _MaxResultPreview);
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

        /// <summary>
        /// Determines whether an event represents visible work output and should replace the initial thinking animation.
        /// Lifecycle-only events do not count because they can arrive immediately before the model has produced user-visible output.
        /// </summary>
        /// <param name="agentEvent">The event to inspect.</param>
        /// <returns>True if the thinking animation should stop.</returns>
        private static bool ShouldStopThinkingAnimation(AgentEvent agentEvent)
        {
            return agentEvent is AssistantTextEvent
                || agentEvent is ToolCallProposedEvent
                || agentEvent is ToolCallApprovedEvent
                || agentEvent is ToolCallCompletedEvent
                || agentEvent is ErrorEvent
                || agentEvent is ContextStatusEvent
                || agentEvent is ContextCompactedEvent
                || agentEvent is HeartbeatEvent;
        }

        #endregion
    }
}
