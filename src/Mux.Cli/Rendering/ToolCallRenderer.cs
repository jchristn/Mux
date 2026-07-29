namespace Mux.Cli.Rendering
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// Renders tool call approval prompts in a compact Claude Code-inspired style.
    /// </summary>
    public static class ToolCallRenderer
    {
        #region Public-Members

        /// <summary>
        /// Prefix used for non-terminal human-facing tool lifecycle log lines.
        /// </summary>
        public const string ToolLogBranchPrefix = "  \u251C ";

        /// <summary>
        /// Prefix used for terminal human-facing tool lifecycle log lines.
        /// </summary>
        public const string ToolLogLeafPrefix = "  \u2514 ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Displays a compact tool call summary and prompts for approval.
        /// </summary>
        /// <param name="toolCall">The tool call to present for approval.</param>
        /// <returns>The user's response string (e.g. "y", "n", "always").</returns>
        public static async Task<string> PromptApprovalAsync(ToolCall toolCall)
        {
            string summary = FormatToolSummary(toolCall.Name, toolCall.Arguments);
            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(FormatToolCallLine(summary))}[/]");
            AnsiConsole.Markup(FormatApprovalPromptMarkup());

            string? response = await Task.Run(() => Console.ReadLine());

            return response?.Trim() ?? "n";
        }

        /// <summary>
        /// Formats the approval prompt markup with a consistent colored box-drawing prefix.
        /// </summary>
        /// <returns>The approval prompt markup.</returns>
        public static string FormatApprovalPromptMarkup()
        {
            return $"[yellow]{Markup.Escape(ToolLogLeafPrefix)}Allow?[/] [[[green]Y[/]/[red]n[/]/[blue]always[/]]] ";
        }

        /// <summary>
        /// Formats a tool call notification line with the standard tool log prefix.
        /// </summary>
        /// <param name="summary">The already formatted tool summary.</param>
        /// <returns>The prefixed notification line.</returns>
        public static string FormatToolCallLine(string summary)
        {
            return $"{ToolLogBranchPrefix}Tool call: {summary}";
        }

        /// <summary>
        /// Formats a tool execution result line with the standard tool log prefix.
        /// </summary>
        /// <param name="toolName">The executed tool name.</param>
        /// <param name="summary">A concise result summary.</param>
        /// <param name="status">The execution status text.</param>
        /// <param name="elapsedMs">The elapsed execution time in milliseconds.</param>
        /// <returns>The prefixed result line.</returns>
        public static string FormatToolExecutionLine(string toolName, string summary, string status, long elapsedMs)
        {
            return $"{ToolLogLeafPrefix}Tool {toolName}: {summary} {status} {elapsedMs}ms";
        }

        /// <summary>
        /// Formats an arbitrary tool lifecycle line with the standard tool log prefix.
        /// </summary>
        /// <param name="line">The unprefixed line.</param>
        /// <param name="isTerminal">Whether this line is the terminal entry in a tool lifecycle group.</param>
        /// <returns>The prefixed line.</returns>
        public static string FormatToolLogLine(string line, bool isTerminal = false)
        {
            return $"{(isTerminal ? ToolLogLeafPrefix : ToolLogBranchPrefix)}{line}";
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Formats a tool call into a concise one-line summary.
        /// </summary>
        /// <param name="toolName">The tool name.</param>
        /// <param name="arguments">The raw JSON arguments string.</param>
        /// <returns>A human-readable summary string.</returns>
        public static string FormatToolSummary(string toolName, string arguments)
        {
            string argSummary = ExtractArgSummary(toolName, arguments);
            if (string.IsNullOrEmpty(argSummary))
            {
                return toolName;
            }
            return $"{toolName}: {argSummary}";
        }

        /// <summary>
        /// Extracts a human-readable argument summary based on the tool name.
        /// </summary>
        private static string ExtractArgSummary(string toolName, string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return string.Empty;
            }

            try
            {
                ToolArgumentSummary? root = JsonSerializer.Deserialize<ToolArgumentSummary>(arguments);
                if (root == null)
                {
                    return string.Empty;
                }

                switch (toolName)
                {
                    case "read_file":
                        return GetShortPath(root.FilePath ?? string.Empty);

                    case "write_file":
                        string writePath = GetShortPath(root.FilePath ?? string.Empty);
                        return writePath;

                    case "edit_file":
                        return GetShortPath(root.FilePath ?? string.Empty);

                    case "multi_edit":
                        string editPath = GetShortPath(root.FilePath ?? string.Empty);
                        if (root.Edits != null)
                        {
                            return $"{editPath} ({root.Edits.Count} edits)";
                        }
                        return editPath;

                    case "delete_file":
                        return GetShortPath(root.FilePath ?? string.Empty);

                    case "file_metadata":
                        return GetShortPath(root.Path ?? string.Empty);

                    case "list_directory":
                        return GetShortPath(root.Path ?? string.Empty);

                    case "manage_directory":
                        string action = root.Action ?? string.Empty;
                        string dirPath = GetShortPath(root.Path ?? string.Empty);
                        string newDirPath = root.NewPath ?? string.Empty;
                        if (action == "rename" && !string.IsNullOrEmpty(newDirPath))
                        {
                            return $"{action} {dirPath} -> {GetShortPath(newDirPath)}";
                        }
                        return $"{action} {dirPath}";

                    case "glob":
                        return root.Pattern ?? string.Empty;

                    case "grep":
                        string pattern = root.Pattern ?? string.Empty;
                        string grepPath = root.Path ?? string.Empty;
                        if (!string.IsNullOrEmpty(grepPath))
                        {
                            return $"\"{pattern}\" in {GetShortPath(grepPath)}";
                        }
                        return $"\"{pattern}\"";

                    case "web_retrieve":
                        return root.Url ?? string.Empty;

                    case "run_process":
                        string cmd = root.Command ?? string.Empty;
                        if (root.Args != null)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder(cmd);
                            foreach (string arg in root.Args)
                            {
                                sb.Append(' ');
                                sb.Append(arg);
                            }
                            string full = sb.ToString();
                            return full.Length > 80 ? full.Substring(0, 77) + "..." : full;
                        }
                        return cmd;

                    default:
                        return arguments.Length > 80 ? arguments.Substring(0, 77) + "..." : arguments;
                }
            }
            catch
            {
                return arguments.Length > 80 ? arguments.Substring(0, 77) + "..." : arguments;
            }
        }

        /// <summary>
        /// Shortens a file path to just the filename or last two path segments.
        /// </summary>
        private static string GetShortPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string fileName = Path.GetFileName(path);
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                string dirName = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(dirName))
                {
                    return dirName + "/" + fileName;
                }
            }
            return fileName;
        }

        #endregion
    }
}
