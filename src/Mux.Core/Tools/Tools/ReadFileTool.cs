namespace Mux.Core.Tools.Tools
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Context;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Tools;

    /// <summary>
    /// Reads a file from the filesystem and returns its contents with line numbers.
    /// </summary>
    public class ReadFileTool : IToolExecutor
    {
        #region Public-Members

        /// <summary>
        /// The unique name of this tool.
        /// </summary>
        public string Name => "read_file";

        /// <summary>
        /// A human-readable description of what this tool does.
        /// </summary>
        public string Description => PromptResolver.Shared.GetEffective("tool.read_file");

        /// <summary>
        /// The JSON Schema object describing the tool's input parameters.
        /// </summary>
        public object ParametersSchema => new
        {
            type = "object",
            properties = new
            {
                file_path = new
                {
                    type = "string",
                    description = "The absolute path to the file to read."
                },
                offset = new
                {
                    type = "integer",
                    description = "The line number to start reading from (1-based). Defaults to 1."
                },
                limit = new
                {
                    type = "integer",
                    description = "The maximum number of lines to read. Defaults to reading the entire file."
                }
            },
            required = new[] { "file_path" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the read_file tool.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="arguments">The parsed JSON arguments containing file_path, offset, and limit.</param>
        /// <param name="workingDirectory">The current working directory for resolving relative paths.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the file contents with line numbers.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                string filePath = GetRequiredString(arguments, "file_path");
                string resolvedPath = ResolvePath(filePath, workingDirectory);

                int offset = GetOptionalInt(arguments, "offset", 1);
                int limit = GetOptionalInt(arguments, "limit", -1);
                bool hasExplicitRange = arguments.TryGetProperty("offset", out _) || arguments.TryGetProperty("limit", out _);

                if (!File.Exists(resolvedPath))
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new { error = "file_not_found", message = $"File not found: {resolvedPath}" })
                    };
                }

                FileInfo fileInfo = new FileInfo(resolvedPath);

                // Beyond the absolute cap the file is refused outright — too large to load even for a map.
                if (fileInfo.Length > ToolSafetyLimits.MaxMappableFileBytes)
                {
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = false,
                        Content = JsonSerializer.Serialize(new
                        {
                            error = "file_too_large",
                            message = $"File size ({fileInfo.Length} bytes) exceeds the maximum readable size ({ToolSafetyLimits.MaxMappableFileBytes} bytes): {resolvedPath}. Use grep to search it, or read_file with offset and limit for a specific range."
                        })
                    };
                }

                // A file over the inline cap, with no explicit range requested, becomes a structural map (or,
                // under truncate/refuse mode, the strict refusal) instead of being read whole. An explicit
                // offset/limit falls through to paged reading below, so the map's "page with offset/limit"
                // advice keeps working.
                if (fileInfo.Length > ToolSafetyLimits.MaxReadFileBytes && !hasExplicitRange)
                {
                    ContextSettings context = SettingsLoader.LoadSettings().Context;
                    ContextSettings.TryNormalizeLargeFileMode(context.LargeFileMode, out string normalizedMode);
                    if (string.Equals(normalizedMode, "truncate", StringComparison.Ordinal))
                    {
                        return new ToolResult
                        {
                            ToolCallId = toolCallId,
                            Success = false,
                            Content = JsonSerializer.Serialize(new
                            {
                                error = "file_too_large",
                                message = $"File size ({fileInfo.Length} bytes) exceeds maximum allowed ({ToolSafetyLimits.MaxReadFileBytes} bytes): {resolvedPath}. Use read_file with offset and limit to read a specific range."
                            })
                        };
                    }

                    string largeContent = await File.ReadAllTextAsync(resolvedPath, cancellationToken).ConfigureAwait(false);
                    FileContextBuilder builder = new FileContextBuilder(null);
                    FileContextResult mapped = await builder.BuildAsync(
                        new FileContextRequest(resolvedPath, largeContent, FileContextMode.Map, 1, 40, context.SummaryChunkLines, null, string.Empty),
                        null,
                        cancellationToken).ConfigureAwait(false);
                    return new ToolResult
                    {
                        ToolCallId = toolCallId,
                        Success = true,
                        Content = mapped.Text
                    };
                }

                string[] lines = await File.ReadAllLinesAsync(resolvedPath, cancellationToken).ConfigureAwait(false);

                int startIndex = Math.Max(0, offset - 1);
                int endIndex = limit > 0 ? Math.Min(lines.Length, startIndex + limit) : lines.Length;

                StringBuilder sb = new StringBuilder();

                for (int i = startIndex; i < endIndex; i++)
                {
                    string lineContent = lines[i].Replace("\r", string.Empty);
                    int lineNumber = i + 1;
                    sb.Append($"{lineNumber,6}\t{lineContent}\n");
                }

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = sb.ToString()
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "permission_denied", message = "Permission denied when reading the file." })
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "read_error", message = ex.Message })
                };
            }
        }

        #endregion

        #region Private-Methods

        private string GetRequiredString(JsonElement arguments, string propertyName)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString()!;
            }

            throw new ArgumentException($"Required parameter '{propertyName}' is missing or not a string.");
        }

        private int GetOptionalInt(JsonElement arguments, string propertyName, int defaultValue)
        {
            if (arguments.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetInt32();
            }

            return defaultValue;
        }

        private string ResolvePath(string filePath, string workingDirectory)
        {
            if (Path.IsPathRooted(filePath))
            {
                return Path.GetFullPath(filePath);
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, filePath));
        }

        #endregion
    }
}
