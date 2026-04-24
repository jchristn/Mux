namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Spectre.Console.Cli;
    using Mux.Core.Settings;
    using Mux.Core.Utility;

    /// <summary>
    /// Settings specific to the print (single-shot) command.
    /// </summary>
    public class PrintSettings : CommonSettings
    {
        #region Public-Members

        /// <summary>
        /// The prompt to send to the agent. If omitted, input is read from stdin.
        /// </summary>
        [Description("The prompt to send to the agent.")]
        [CommandArgument(0, "[prompt]")]
        public string? Prompt { get; set; }

        /// <summary>
        /// Flag indicating single-shot print mode (enables piping via <c>--print</c>).
        /// </summary>
        [Description("Run in single-shot print mode.")]
        [CommandOption("-p|--print")]
        public bool Print { get; set; }

        #endregion
    }

    /// <summary>
    /// Single-shot command that runs a prompt through the agent and prints the result to stdout.
    /// Designed for use in pipelines and non-interactive contexts.
    /// </summary>
    public class PrintCommand : AsyncCommand<PrintSettings>
    {
        #region Public-Methods

        /// <summary>
        /// Executes the single-shot print command.
        /// </summary>
        /// <param name="context">The command context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">The resolved command settings.</param>
        /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
        /// <returns>Exit code: 0 success, 1 error, 2 tool denied.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, PrintSettings settings, CancellationToken cancellationToken)
        {
            OutputFormatEnum outputFormat;
            try
            {
                outputFormat = CommandRuntimeResolver.ParseOutputFormat(settings.OutputFormat, OutputFormatEnum.Text, OutputFormatEnum.Jsonl);
            }
            catch (Exception ex)
            {
                EmitBootstrapError(settings, ex.Message);
                return 1;
            }

            string prompt = ResolvePrompt(settings);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                EmitBootstrapError(settings, "No prompt provided. Pass a prompt argument or pipe input via stdin.");
                return 1;
            }

            ResolvedRuntime runtime;
            try
            {
                runtime = CommandRuntimeResolver.ResolveRuntime(settings, "print", supportsMcp: false, allowAskApproval: false);
            }
            catch (Exception ex)
            {
                EmitBootstrapError(settings, ex.Message);
                return 1;
            }

            AgentLoopOptions loopOptions = new AgentLoopOptions(runtime.Endpoint)
            {
                SystemPrompt = runtime.SystemPrompt,
                ApprovalPolicy = runtime.ApprovalPolicy,
                WorkingDirectory = runtime.WorkingDirectory,
                MaxIterations = runtime.MuxSettings.MaxAgentIterations,
                CommandName = runtime.Metadata.CommandName,
                ConfigDirectory = runtime.Metadata.ConfigDirectory,
                EndpointSelectionSource = runtime.Metadata.EndpointSelectionSource,
                CliOverridesApplied = runtime.Metadata.CliOverridesApplied,
                McpSupported = runtime.Capabilities.McpSupported,
                McpConfigured = runtime.Capabilities.McpConfigured,
                McpServerCount = runtime.Capabilities.McpServerCount,
                BuiltInToolCount = runtime.Capabilities.BuiltInToolCount,
                EffectiveToolCount = runtime.Capabilities.EffectiveToolCount,
                Verbose = settings.Verbose
            };

            int exitCode = 0;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                try
                {
                    AgentLoop agentLoop = new AgentLoop(loopOptions);
                    IAsyncEnumerable<AgentEvent> events = agentLoop.RunAsync(prompt, cts.Token);

                    int stepCount = 0;

                    await foreach (AgentEvent agentEvent in events.WithCancellation(cts.Token))
                    {
                        if (agentEvent is ErrorEvent structuredErrorEvent)
                        {
                            EnrichErrorEvent(structuredErrorEvent, runtime);
                        }

                        if (outputFormat == OutputFormatEnum.Jsonl)
                        {
                            Console.WriteLine(StructuredOutputFormatter.FormatEvent(agentEvent));
                        }

                        switch (agentEvent)
                        {
                            case AssistantTextEvent textEvent:
                                if (outputFormat == OutputFormatEnum.Text)
                                {
                                    Console.Write(textEvent.Text);
                                }
                                break;

                            case HeartbeatEvent heartbeatEvent:
                                stepCount = heartbeatEvent.StepNumber;
                                if (outputFormat == OutputFormatEnum.Text)
                                {
                                    Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Working... (step {heartbeatEvent.StepNumber})"));
                                }
                                break;

                            case ErrorEvent errorEvent:
                                if (outputFormat == OutputFormatEnum.Text)
                                {
                                    Console.Error.WriteLine(ConsoleMessageStyler.Failure($"Error: {errorEvent.Code}: {errorEvent.Message}"));
                                }
                                if (exitCode == 0 || errorEvent.Code != "tool_call_denied")
                                {
                                    exitCode = errorEvent.Code == "tool_call_denied" ? 2 : 1;
                                }
                                break;

                            case ToolCallProposedEvent proposedEvent:
                                stepCount++;
                                if (outputFormat == OutputFormatEnum.Text)
                                {
                                    Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Working... (step {stepCount})"));
                                    if (settings.Verbose)
                                    {
                                        Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Tool call: {proposedEvent.ToolCall.Name}"));
                                    }
                                }
                                if (runtime.ApprovalPolicy == ApprovalPolicyEnum.Deny)
                                {
                                    if (outputFormat == OutputFormatEnum.Text)
                                    {
                                        Console.Error.WriteLine(
                                            ConsoleMessageStyler.Failure($"Tool call denied in non-interactive mode: {proposedEvent.ToolCall.Name}"));
                                    }
                                    if (exitCode == 0) exitCode = 2;
                                }
                                break;

                            case ToolCallCompletedEvent completedEvent:
                                if (outputFormat == OutputFormatEnum.Text && settings.Verbose)
                                {
                                    string resultPreview = completedEvent.Result?.Content ?? "(no content)";
                                    if (resultPreview.Length > 200)
                                    {
                                        resultPreview = resultPreview.Substring(0, 200) + "...";
                                    }
                                    Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Tool result ({completedEvent.ToolCallId}): {resultPreview}"));
                                }
                                break;

                            default:
                                break;
                        }
                    }

                    if (outputFormat == OutputFormatEnum.Text)
                    {
                        Console.WriteLine();
                    }
                }
                catch (OperationCanceledException)
                {
                    EmitRuntimeError(outputFormat, CreateRuntimeError("cancelled", "Operation was cancelled.", runtime));
                    exitCode = 1;
                }
                catch (Exception ex)
                {
                    EmitRuntimeError(outputFormat, CreateRuntimeError("print_error", ex.Message, runtime));
                    exitCode = 1;
                }
            }

            return exitCode;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Resolves the prompt from the command argument or stdin.
        /// </summary>
        /// <param name="settings">The print command settings.</param>
        /// <returns>The resolved prompt string.</returns>
        private static string ResolvePrompt(PrintSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Prompt))
            {
                return settings.Prompt!;
            }

            if (Console.IsInputRedirected)
            {
                return Console.In.ReadToEnd();
            }

            return string.Empty;
        }

        private static void EmitBootstrapError(PrintSettings settings, string message)
        {
            OutputFormatEnum format = string.Equals(settings.OutputFormat, "jsonl", StringComparison.OrdinalIgnoreCase)
                ? OutputFormatEnum.Jsonl
                : OutputFormatEnum.Text;

            ErrorEvent errorEvent = StructuredOutputFormatter.CreateErrorEvent(ClassifyBootstrapErrorCode(message), message);
            errorEvent.CommandName = "print";
            errorEvent.FailureCategory = "configuration";

            TryPopulateBootstrapMetadata(errorEvent, settings);

            EmitRuntimeError(format, errorEvent);
        }

        private static void EmitRuntimeError(OutputFormatEnum outputFormat, ErrorEvent errorEvent)
        {
            if (outputFormat == OutputFormatEnum.Jsonl)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatEvent(errorEvent));
            }
            else
            {
                Console.Error.WriteLine(ConsoleMessageStyler.Failure($"Error: {errorEvent.Message}"));
            }
        }

        private static ErrorEvent CreateRuntimeError(string code, string message, ResolvedRuntime runtime)
        {
            ErrorEvent errorEvent = StructuredOutputFormatter.CreateErrorEvent(code, message);
            EnrichErrorEvent(errorEvent, runtime);
            return errorEvent;
        }

        private static void EnrichErrorEvent(ErrorEvent errorEvent, ResolvedRuntime runtime)
        {
            errorEvent.FailureCategory = string.IsNullOrWhiteSpace(errorEvent.FailureCategory)
                ? ClassifyFailureCategory(errorEvent.Code)
                : errorEvent.FailureCategory;
            errorEvent.EndpointName = string.IsNullOrWhiteSpace(errorEvent.EndpointName) ? runtime.Endpoint.Name : errorEvent.EndpointName;
            errorEvent.AdapterType = string.IsNullOrWhiteSpace(errorEvent.AdapterType) ? runtime.Endpoint.AdapterType.ToString() : errorEvent.AdapterType;
            errorEvent.BaseUrl = string.IsNullOrWhiteSpace(errorEvent.BaseUrl) ? runtime.Endpoint.BaseUrl : errorEvent.BaseUrl;
            errorEvent.Model = string.IsNullOrWhiteSpace(errorEvent.Model) ? runtime.Endpoint.Model : errorEvent.Model;
            errorEvent.CommandName = string.IsNullOrWhiteSpace(errorEvent.CommandName) ? runtime.Metadata.CommandName : errorEvent.CommandName;
            errorEvent.ConfigDirectory = string.IsNullOrWhiteSpace(errorEvent.ConfigDirectory) ? runtime.Metadata.ConfigDirectory : errorEvent.ConfigDirectory;
            errorEvent.EndpointSelectionSource = string.IsNullOrWhiteSpace(errorEvent.EndpointSelectionSource)
                ? runtime.Metadata.EndpointSelectionSource
                : errorEvent.EndpointSelectionSource;
            if (errorEvent.CliOverridesApplied.Count == 0)
            {
                errorEvent.CliOverridesApplied = new List<string>(runtime.Metadata.CliOverridesApplied);
            }
        }

        private static void TryPopulateBootstrapMetadata(ErrorEvent errorEvent, PrintSettings settings)
        {
            try
            {
                SettingsLoader.EnsureConfigDirectory();
                errorEvent.ConfigDirectory = SettingsLoader.GetConfigDirectory();
                errorEvent.CliOverridesApplied = GetCliOverrides(settings);
                if (!string.IsNullOrWhiteSpace(settings.Endpoint))
                {
                    errorEvent.EndpointSelectionSource = "named_endpoint";
                    errorEvent.EndpointName = settings.Endpoint!;
                }

                if (!string.IsNullOrWhiteSpace(settings.Model))
                {
                    errorEvent.Model = settings.Model!;
                }

                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    errorEvent.BaseUrl = settings.BaseUrl!;
                }

                if (!string.IsNullOrWhiteSpace(settings.AdapterType))
                {
                    errorEvent.AdapterType = settings.AdapterType!;
                }
            }
            catch
            {
                // Best-effort only. Bootstrap error emission should not fail while collecting metadata.
            }
        }

        private static List<string> GetCliOverrides(CommonSettings settings)
        {
            List<string> overrides = new List<string>();

            if (!string.IsNullOrWhiteSpace(settings.Endpoint)) overrides.Add("endpoint");
            if (!string.IsNullOrWhiteSpace(settings.Model)) overrides.Add("model");
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) overrides.Add("baseUrl");
            if (!string.IsNullOrWhiteSpace(settings.AdapterType)) overrides.Add("adapterType");
            if (settings.Temperature.HasValue) overrides.Add("temperature");
            if (settings.MaxTokens.HasValue) overrides.Add("maxTokens");
            if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory)) overrides.Add("workingDirectory");
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) overrides.Add("systemPrompt");
            if (settings.Yolo) overrides.Add("yolo");
            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy)) overrides.Add("approvalPolicy");

            return overrides;
        }

        private static string ClassifyBootstrapErrorCode(string message)
        {
            if (message.Contains("No endpoint named", StringComparison.OrdinalIgnoreCase))
            {
                return "endpoint_not_found";
            }

            if (message.Contains("MCP is only supported in interactive mode", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Approval policy 'ask' is not supported", StringComparison.OrdinalIgnoreCase))
            {
                return "unsupported_option";
            }

            if (message.Contains("Unsupported output format", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Unsupported approval policy", StringComparison.OrdinalIgnoreCase)
                || message.Contains("No prompt provided", StringComparison.OrdinalIgnoreCase))
            {
                return "invalid_argument";
            }

            return "print_error";
        }

        private static string ClassifyFailureCategory(string code)
        {
            return code switch
            {
                "endpoint_not_found" => "configuration",
                "unsupported_option" => "configuration",
                "invalid_argument" => "configuration",
                "config_error" => "configuration",
                "cancelled" => "cancellation",
                "tool_call_denied" => "approval",
                "approval_error" => "approval",
                "tool_execution_error" => "tool",
                "llm_connection_error" => "network",
                "llm_error" => "backend",
                "llm_stream_error" => "backend",
                "max_iterations_reached" => "runtime",
                "print_error" => "unknown",
                _ => "unknown"
            };
        }

        #endregion
    }
}
