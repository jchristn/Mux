namespace Mux.Cli.Rendering
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Spectre.Console;

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
            AnsiConsole.Markup($"{ToolLogLeafPrefix}Allow? [[[green]Y[/]/[red]n[/]/[blue]always[/]]] ");

            string? response = await Task.Run(() => Console.ReadLine());

            return response?.Trim() ?? "n";
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
                JsonDocument doc = JsonDocument.Parse(arguments);
                JsonElement root = doc.RootElement;

                switch (toolName)
                {
                    case "read_file":
                        return GetShortPath(GetStringProp(root, "file_path"));

                    case "write_file":
                        string writePath = GetShortPath(GetStringProp(root, "file_path"));
                        return writePath;

                    case "edit_file":
                        return GetShortPath(GetStringProp(root, "file_path"));

                    case "multi_edit":
                        string editPath = GetShortPath(GetStringProp(root, "file_path"));
                        if (root.TryGetProperty("edits", out JsonElement editsEl) && editsEl.ValueKind == JsonValueKind.Array)
                        {
                            return $"{editPath} ({editsEl.GetArrayLength()} edits)";
                        }
                        return editPath;

                    case "delete_file":
                        return GetShortPath(GetStringProp(root, "file_path"));

                    case "file_metadata":
                        return GetShortPath(GetStringProp(root, "path"));

                    case "list_directory":
                        return GetShortPath(GetStringProp(root, "path"));

                    case "manage_directory":
                        string action = GetStringProp(root, "action");
                        string dirPath = GetShortPath(GetStringProp(root, "path"));
                        string newDirPath = GetStringProp(root, "new_path");
                        if (action == "rename" && !string.IsNullOrEmpty(newDirPath))
                        {
                            return $"{action} {dirPath} -> {GetShortPath(newDirPath)}";
                        }
                        return $"{action} {dirPath}";

                    case "glob":
                        return GetStringProp(root, "pattern");

                    case "grep":
                        string pattern = GetStringProp(root, "pattern");
                        string grepPath = GetStringProp(root, "path");
                        if (!string.IsNullOrEmpty(grepPath))
                        {
                            return $"\"{pattern}\" in {GetShortPath(grepPath)}";
                        }
                        return $"\"{pattern}\"";

                    case "web_retrieve":
                        return GetStringProp(root, "url");

                    case "run_process":
                        string cmd = GetStringProp(root, "command");
                        if (root.TryGetProperty("args", out JsonElement argsEl) && argsEl.ValueKind == JsonValueKind.Array)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder(cmd);
                            foreach (JsonElement arg in argsEl.EnumerateArray())
                            {
                                if (arg.ValueKind == JsonValueKind.String)
                                {
                                    sb.Append(' ');
                                    sb.Append(arg.GetString());
                                }
                            }
                            string full = sb.ToString();
                            return full.Length > 80 ? full.Substring(0, 77) + "..." : full;
                        }
                        return cmd;

                    default:
                        // For unknown/MCP tools, show compact JSON
                        string compact = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = false });
                        return compact.Length > 80 ? compact.Substring(0, 77) + "..." : compact;
                }
            }
            catch
            {
                return arguments.Length > 80 ? arguments.Substring(0, 77) + "..." : arguments;
            }
        }

        /// <summary>
        /// Gets a string property from a JSON element, or empty string if not found.
        /// </summary>
        private static string GetStringProp(JsonElement root, string name)
        {
            if (root.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString() ?? string.Empty;
            }
            return string.Empty;
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
