namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
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
                ResolvedRuntime runtime = CommandRuntimeResolver.ResolveRuntime(settings);
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
                    Console.WriteLine(StructuredOutputFormatter.FormatObject(new ProbeResult
                    {
                        Success = false,
                        ErrorCode = "probe_error",
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
                Model = runtime.Endpoint.Model
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
                result.ErrorCode = "probe_error";
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
                    ErrorMessage = message
                }));
            }
            else
            {
                Console.Error.WriteLine($"[error] {message}");
            }
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
        /// Machine-readable error code when the probe fails.
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable error message when the probe fails.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Elapsed duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; }
    }
}
