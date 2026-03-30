namespace Mux.Cli.Rendering
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Spectre.Console;

    /// <summary>
    /// Renders agent events to the terminal in a compact Claude Code-inspired style.
    /// </summary>
    public static class EventRenderer
    {
        #region Private-Members

        private static readonly int _MaxResultPreview = 300;
        private static readonly int _StatusLineWidth = 80;

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

            AnsiConsole.Markup("[dim]Thinking...[/]");

            await foreach (AgentEvent agentEvent in events)
            {
                if (waitingForFirstEvent)
                {
                    ClearStatusLine();
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
                        // Always render the tool call proposal line
                        RenderToolProposal(proposedEvent);
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

            if (waitingForFirstEvent)
            {
                ClearStatusLine();
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
            ClearStatusLine();
            AnsiConsole.Markup($"[dim]{Markup.Escape(message)}[/]");
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Clears the current status/thinking line.
        /// </summary>
        private static void ClearStatusLine()
        {
            Console.Write("\r");
            Console.Write(new string(' ', _StatusLineWidth));
            Console.Write("\r");
        }

        /// <summary>
        /// Renders a tool call proposal with a compact one-line summary.
        /// </summary>
        /// <param name="proposedEvent">The tool call proposed event.</param>
        private static void RenderToolProposal(ToolCallProposedEvent proposedEvent)
        {
            string summary = ToolCallRenderer.FormatToolSummary(
                proposedEvent.ToolCall.Name,
                proposedEvent.ToolCall.Arguments);
            AnsiConsole.MarkupLine($"  [cyan]●[/] [bold]{Markup.Escape(summary)}[/]");
        }

        /// <summary>
        /// Renders a tool result on the same line area as the proposal.
        /// </summary>
        /// <param name="completedEvent">The tool call completed event.</param>
        private static void RenderToolResult(ToolCallCompletedEvent completedEvent)
        {
            if (completedEvent.Result.Success)
            {
                string summary = SummarizeResult(completedEvent.Result.Content);
                AnsiConsole.MarkupLine($"    [green]✓[/] [dim]{Markup.Escape(summary)}[/]");
            }
            else
            {
                string errorContent = TruncateString(completedEvent.Result.Content, _MaxResultPreview);
                AnsiConsole.MarkupLine($"    [red]✗ {Markup.Escape(errorContent)}[/]");
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
                            return $"{fileName} ({eaEl.GetInt32()} edits applied)";
                        }
                        return fileName;
                    }
                    if (root.TryGetProperty("path", out JsonElement dirPathEl))
                    {
                        return System.IO.Path.GetFileName(dirPathEl.GetString() ?? "") + "/";
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
