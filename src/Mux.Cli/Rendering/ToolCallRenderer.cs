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
        #region Public-Methods

        /// <summary>
        /// Displays a compact tool call summary and prompts for approval.
        /// </summary>
        /// <param name="toolCall">The tool call to present for approval.</param>
        /// <returns>The user's response string (e.g. "y", "n", "always").</returns>
        public static async Task<string> PromptApprovalAsync(ToolCall toolCall)
        {
            AnsiConsole.WriteLine();
            string summary = FormatToolSummary(toolCall.Name, toolCall.Arguments);
            AnsiConsole.MarkupLine($"  [cyan]●[/] [bold]{Markup.Escape(summary)}[/]");

            AnsiConsole.Markup("    Allow? [[[green]Y[/]/[red]n[/]/[blue]always[/]]] ");

            string? response = await Task.Run(() => Console.ReadLine());

            return response?.Trim() ?? "n";
        }

        /// <summary>
        /// Displays a compact tool call summary without prompting (for auto-approve mode).
        /// Called by EventRenderer when no approval is needed.
        /// </summary>
        /// <param name="toolCall">The tool call to display.</param>
        public static void RenderAutoApproved(ToolCall toolCall)
        {
            AnsiConsole.WriteLine();
            string summary = FormatToolSummary(toolCall.Name, toolCall.Arguments);
            AnsiConsole.MarkupLine($"  [cyan]●[/] [bold]{Markup.Escape(summary)}[/]");
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Formats a tool call into a concise one-line summary.
        /// </summary>
        private static string FormatToolSummary(string toolName, string arguments)
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

                    case "list_directory":
                        return GetShortPath(GetStringProp(root, "path"));

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
