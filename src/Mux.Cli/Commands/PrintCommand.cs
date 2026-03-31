namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
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
            string prompt = ResolvePrompt(settings);

            if (string.IsNullOrWhiteSpace(prompt))
            {
                Console.Error.WriteLine("[error] No prompt provided. Pass a prompt argument or pipe input via stdin.");
                return 1;
            }

            SettingsLoader.EnsureConfigDirectory();
            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
            MuxSettings muxSettings = SettingsLoader.LoadSettings();

            EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(
                endpoints,
                settings.Endpoint,
                settings.Model,
                settings.BaseUrl,
                settings.AdapterType,
                settings.Temperature,
                settings.MaxTokens);

            string workingDirectory = settings.WorkingDirectory ?? Directory.GetCurrentDirectory();

            Mux.Core.Tools.BuiltInToolRegistry toolRegistry = new Mux.Core.Tools.BuiltInToolRegistry();
            List<ToolDefinition> builtInTools = toolRegistry.GetToolDefinitions();
            bool toolsEnabled = endpoint.Quirks?.SupportsTools ?? true;

            StringBuilder toolDescBuilder = new StringBuilder();
            if (toolsEnabled)
            {
                foreach (ToolDefinition tool in builtInTools)
                {
                    toolDescBuilder.AppendLine($"- {tool.Name}: {tool.Description}");
                }
            }

            string systemPrompt = SettingsLoader.LoadSystemPrompt(settings.SystemPrompt, muxSettings);

            if (!toolsEnabled)
            {
                systemPrompt = "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
                    "in their project.\n\n" +
                    "Your current working directory is: {WorkingDirectory}\n\n" +
                    "Guidelines:\n" +
                    "- Explain your reasoning when making non-trivial suggestions.\n" +
                    "- If a task is ambiguous, ask for clarification before proceeding.";
            }

            systemPrompt = systemPrompt
                .Replace("{WorkingDirectory}", workingDirectory)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd());

            ApprovalPolicyEnum approvalPolicy = settings.Yolo
                ? ApprovalPolicyEnum.AutoApprove
                : ApprovalPolicyEnum.Deny;

            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy))
            {
                if (Enum.TryParse<ApprovalPolicyEnum>(settings.ApprovalPolicy, true, out ApprovalPolicyEnum parsed))
                {
                    approvalPolicy = parsed;
                }
            }

            AgentLoopOptions loopOptions = new AgentLoopOptions(endpoint)
            {
                SystemPrompt = systemPrompt,
                ApprovalPolicy = approvalPolicy,
                WorkingDirectory = workingDirectory,
                MaxIterations = muxSettings.MaxAgentIterations,
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
                        switch (agentEvent)
                        {
                            case AssistantTextEvent textEvent:
                                Console.Write(textEvent.Text);
                                break;

                            case HeartbeatEvent heartbeatEvent:
                                stepCount = heartbeatEvent.StepNumber;
                                Console.Error.WriteLine($"[mux] working... (step {heartbeatEvent.StepNumber})");
                                break;

                            case ErrorEvent errorEvent:
                                Console.Error.WriteLine($"[error] {errorEvent.Code}: {errorEvent.Message}");
                                if (exitCode == 0) exitCode = 1;
                                break;

                            case ToolCallProposedEvent proposedEvent:
                                stepCount++;
                                Console.Error.WriteLine($"[mux] working... (step {stepCount})");
                                if (settings.Verbose)
                                {
                                    Console.Error.WriteLine($"[mux] tool call: {proposedEvent.ToolCall.Name}");
                                }
                                if (approvalPolicy == ApprovalPolicyEnum.Deny)
                                {
                                    Console.Error.WriteLine(
                                        $"[denied] Tool call denied in non-interactive mode: {proposedEvent.ToolCall.Name}");
                                    if (exitCode == 0) exitCode = 2;
                                }
                                break;

                            case ToolCallCompletedEvent completedEvent:
                                if (settings.Verbose)
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

                    Console.WriteLine();
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine("[cancelled] Operation was cancelled.");
                    exitCode = 1;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[error] {ex.Message}");
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

        #endregion
    }
}
