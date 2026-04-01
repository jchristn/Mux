namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Spectre.Console.Cli;

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
                                    Console.Error.WriteLine($"[mux] working... (step {heartbeatEvent.StepNumber})");
                                }
                                break;

                            case ErrorEvent errorEvent:
                                if (outputFormat == OutputFormatEnum.Text)
                                {
                                    Console.Error.WriteLine($"[error] {errorEvent.Code}: {errorEvent.Message}");
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
                                    Console.Error.WriteLine($"[mux] working... (step {stepCount})");
                                    if (settings.Verbose)
                                    {
                                        Console.Error.WriteLine($"[mux] tool call: {proposedEvent.ToolCall.Name}");
                                    }
                                }
                                if (runtime.ApprovalPolicy == Mux.Core.Enums.ApprovalPolicyEnum.Deny)
                                {
                                    if (outputFormat == OutputFormatEnum.Text)
                                    {
                                        Console.Error.WriteLine(
                                            $"[denied] Tool call denied in non-interactive mode: {proposedEvent.ToolCall.Name}");
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
                                    Console.Error.WriteLine($"[mux] tool result ({completedEvent.ToolCallId}): {resultPreview}");
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
                    EmitRuntimeError(outputFormat, "cancelled", "Operation was cancelled.");
                    exitCode = 1;
                }
                catch (Exception ex)
                {
                    EmitRuntimeError(outputFormat, "print_error", ex.Message);
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

            EmitRuntimeError(format, ClassifyBootstrapErrorCode(message), message);
        }

        private static void EmitRuntimeError(OutputFormatEnum outputFormat, string code, string message)
        {
            if (outputFormat == OutputFormatEnum.Jsonl)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatEvent(
                    StructuredOutputFormatter.CreateErrorEvent(code, message)));
            }
            else
            {
                Console.Error.WriteLine($"[error] {message}");
            }
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

        #endregion
    }
}
