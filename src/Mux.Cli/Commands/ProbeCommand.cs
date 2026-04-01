namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings specific to the probe command.
    /// </summary>
    public class ProbeSettings : CommonSettings
    {
        #region Public-Members

        /// <summary>
        /// Probe prompt used for backend validation.
        /// </summary>
        [Description("Optional probe prompt override.")]
        [CommandOption("--probe-prompt")]
        public string? ProbePrompt { get; set; }

        #endregion
    }

    /// <summary>
    /// Lightweight health-check command that validates config, backend reachability, auth, and model access.
    /// </summary>
    public class ProbeCommand : AsyncCommand<ProbeSettings>
    {
        #region Public-Methods

        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, ProbeSettings settings, CancellationToken cancellationToken)
        {
            OutputFormatEnum outputFormat;
            try
            {
                outputFormat = CommandRuntimeResolver.ParseOutputFormat(settings.OutputFormat, OutputFormatEnum.Text, OutputFormatEnum.Json);
            }
            catch (Exception ex)
            {
                EmitBootstrapError(settings.OutputFormat, ex.Message);
                return 1;
            }

            try
            {
                ResolvedRuntime runtime = CommandRuntimeResolver.ResolveRuntime(settings, "probe", supportsMcp: false, allowAskApproval: false);
                ProbeResult result = await RunProbeAsync(runtime, settings, cancellationToken).ConfigureAwait(false);

                if (outputFormat == OutputFormatEnum.Json)
                {
                    Console.WriteLine(StructuredOutputFormatter.FormatObject(result));
                }
                else
                {
                    if (result.Success)
                    {
                        Console.WriteLine($"[ok] Probe succeeded for endpoint '{result.EndpointName}' using model '{result.Model}'.");
                        Console.WriteLine($"Base URL: {result.BaseUrl}");
                        Console.WriteLine($"Duration: {result.DurationMs}ms");
                        if (!string.IsNullOrWhiteSpace(result.ResponsePreview))
                        {
                            Console.WriteLine($"Response: {result.ResponsePreview}");
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine($"[error] Probe failed for endpoint '{result.EndpointName}': {result.ErrorMessage}");
                    }
                }

                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                if (outputFormat == OutputFormatEnum.Json)
                {
                    ProbeFailureInfo failure = ClassifyProbeFailure(ex);
                    Console.WriteLine(StructuredOutputFormatter.FormatObject(new ProbeResult
                    {
                        Success = false,
                        ErrorCode = failure.Code,
                        FailureCategory = failure.Category,
                        ErrorMessage = ex.Message
                    }));
                }
                else
                {
                    Console.Error.WriteLine($"[error] {ex.Message}");
                }

                return 1;
            }
        }

        #endregion

        #region Private-Methods

        private static async Task<ProbeResult> RunProbeAsync(ResolvedRuntime runtime, ProbeSettings settings, CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ProbeResult result = new ProbeResult
            {
                Success = false,
                EndpointName = runtime.Endpoint.Name,
                AdapterType = runtime.Endpoint.AdapterType.ToString(),
                BaseUrl = runtime.Endpoint.BaseUrl,
                Model = runtime.Endpoint.Model,
                CommandName = runtime.Metadata.CommandName,
                ConfigDirectory = runtime.Metadata.ConfigDirectory,
                EndpointSelectionSource = runtime.Metadata.EndpointSelectionSource,
                CliOverridesApplied = runtime.Metadata.CliOverridesApplied,
                EndpointsFilePresent = runtime.Metadata.EndpointsFilePresent,
                SettingsFilePresent = runtime.Metadata.SettingsFilePresent,
                McpServersFilePresent = runtime.Metadata.McpServersFilePresent,
                BuiltInToolCount = runtime.Capabilities.BuiltInToolCount,
                EffectiveToolCount = runtime.Capabilities.EffectiveToolCount,
                ToolsEnabled = runtime.Capabilities.ToolsEnabled,
                McpSupported = runtime.Capabilities.McpSupported,
                McpConfigured = runtime.Capabilities.McpConfigured,
                McpServerCount = runtime.Capabilities.McpServerCount
            };

            try
            {
                using LlmClient client = new LlmClient(runtime.Endpoint);
                List<ConversationMessage> messages = new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = Mux.Core.Enums.RoleEnum.System,
                        Content = "You are mux probe mode. Reply with a brief confirmation that includes the word OK."
                    },
                    new ConversationMessage
                    {
                        Role = Mux.Core.Enums.RoleEnum.User,
                        Content = string.IsNullOrWhiteSpace(settings.ProbePrompt)
                            ? "Respond with OK and a short confirmation."
                            : settings.ProbePrompt!
                    }
                };

                ConversationMessage response = await client.SendAsync(
                    messages,
                    new List<ToolDefinition>(),
                    cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                result.Success = true;
                result.ResponsePreview = response.Content ?? string.Empty;
                result.DurationMs = stopwatch.ElapsedMilliseconds;
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.DurationMs = stopwatch.ElapsedMilliseconds;
                ProbeFailureInfo failure = ClassifyProbeFailure(ex);
                result.ErrorCode = failure.Code;
                result.FailureCategory = failure.Category;
                result.ErrorMessage = ex.Message;
                return result;
            }
        }

        private static void EmitBootstrapError(string? outputFormatValue, string message)
        {
            bool wantsJson = string.Equals(outputFormatValue, "json", StringComparison.OrdinalIgnoreCase);
            if (wantsJson)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new ProbeResult
                {
                    Success = false,
                    ErrorCode = "probe_error",
                    FailureCategory = "bootstrap",
                    ErrorMessage = message
                }));
            }
            else
            {
                Console.Error.WriteLine($"[error] {message}");
            }
        }

        private static ProbeFailureInfo ClassifyProbeFailure(Exception ex)
        {
            if (ex is TimeoutException
                || ex is TaskCanceledException
                || ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return new ProbeFailureInfo("probe_timeout", "timeout");
            }

            if (ex is OperationCanceledException)
            {
                return new ProbeFailureInfo("probe_cancelled", "cancellation");
            }

            if (ex is InvalidOperationException invalidOperation)
            {
                if (invalidOperation.Message.Contains("No endpoint named", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProbeFailureInfo("endpoint_not_found", "configuration");
                }

                if (invalidOperation.Message.Contains("MCP is only supported in interactive mode", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProbeFailureInfo("unsupported_option", "configuration");
                }

                if (invalidOperation.Message.Contains("Approval policy 'ask' is not supported", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProbeFailureInfo("unsupported_option", "configuration");
                }

                if (invalidOperation.Message.Contains("Unsupported output format", StringComparison.OrdinalIgnoreCase)
                    || invalidOperation.Message.Contains("Unsupported approval policy", StringComparison.OrdinalIgnoreCase))
                {
                    return new ProbeFailureInfo("invalid_argument", "configuration");
                }

                return new ProbeFailureInfo("config_error", "configuration");
            }

            if (ex is HttpRequestException httpException)
            {
                if (httpException.StatusCode.HasValue)
                {
                    return ClassifyHttpStatus(httpException.StatusCode.Value);
                }

                return new ProbeFailureInfo("backend_unreachable", "network");
            }

            return new ProbeFailureInfo("probe_error", "unknown");
        }

        private static ProbeFailureInfo ClassifyHttpStatus(HttpStatusCode statusCode)
        {
            return (int)statusCode switch
            {
                400 => new ProbeFailureInfo("invalid_request", "request"),
                401 => new ProbeFailureInfo("auth_error", "authentication"),
                403 => new ProbeFailureInfo("auth_error", "authentication"),
                404 => new ProbeFailureInfo("not_found", "request"),
                408 => new ProbeFailureInfo("backend_timeout", "timeout"),
                429 => new ProbeFailureInfo("rate_limited", "capacity"),
                >= 500 => new ProbeFailureInfo("backend_error", "backend"),
                _ => new ProbeFailureInfo("http_error", "http")
            };
        }

        #endregion
    }

    /// <summary>
    /// Machine-readable probe result payload.
    /// </summary>
    public class ProbeResult
    {
        /// <summary>
        /// Whether the probe succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Effective endpoint name.
        /// </summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>
        /// Effective adapter type.
        /// </summary>
        public string AdapterType { get; set; } = string.Empty;

        /// <summary>
        /// Effective base URL.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Effective model identifier.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Probe response preview when successful.
        /// </summary>
        public string ResponsePreview { get; set; } = string.Empty;

        /// <summary>
        /// Command mode used for the probe.
        /// </summary>
        public string CommandName { get; set; } = string.Empty;

        /// <summary>
        /// Effective mux configuration directory.
        /// </summary>
        public string ConfigDirectory { get; set; } = string.Empty;

        /// <summary>
        /// How mux selected the effective endpoint.
        /// </summary>
        public string EndpointSelectionSource { get; set; } = string.Empty;

        /// <summary>
        /// CLI override categories applied to the resolved runtime.
        /// </summary>
        public List<string> CliOverridesApplied { get; set; } = new List<string>();

        /// <summary>
        /// Whether endpoints.json exists in the active config directory.
        /// </summary>
        public bool EndpointsFilePresent { get; set; }

        /// <summary>
        /// Whether settings.json exists in the active config directory.
        /// </summary>
        public bool SettingsFilePresent { get; set; }

        /// <summary>
        /// Whether mcp-servers.json exists in the active config directory.
        /// </summary>
        public bool McpServersFilePresent { get; set; }

        /// <summary>
        /// Whether built-in tool calling is enabled for the selected endpoint.
        /// </summary>
        public bool ToolsEnabled { get; set; }

        /// <summary>
        /// Number of built-in tools compiled into mux.
        /// </summary>
        public int BuiltInToolCount { get; set; }

        /// <summary>
        /// Number of tools effectively available for this endpoint.
        /// </summary>
        public int EffectiveToolCount { get; set; }

        /// <summary>
        /// Whether this command mode supports MCP integration.
        /// </summary>
        public bool McpSupported { get; set; }

        /// <summary>
        /// Whether MCP servers are configured in the active config directory.
        /// </summary>
        public bool McpConfigured { get; set; }

        /// <summary>
        /// Number of configured MCP servers.
        /// </summary>
        public int McpServerCount { get; set; }

        /// <summary>
        /// Machine-readable error code when the probe fails.
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Machine-readable failure category when the probe fails.
        /// </summary>
        public string FailureCategory { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable error message when the probe fails.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Elapsed duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; }
    }

    internal sealed class ProbeFailureInfo
    {
        public ProbeFailureInfo(string code, string category)
        {
            Code = code;
            Category = category;
        }

        public string Code { get; }

        public string Category { get; }
    }
}
