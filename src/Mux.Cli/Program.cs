namespace Mux.Cli
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Commands;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using TUIKit.Terminal;

    /// <summary>
    /// Entry point for the mux CLI application.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Application entry point. Dispatches the mux command pipeline.
        /// </summary>
        /// <param name="args">Command-line arguments.</param>
        /// <returns>The process exit code.</returns>
        public static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            bool isVersionRequest = args.Any(a => a == "--version" || a == "/version")
                || (args.Length == 1 && args[0] == "-v");
            if (isVersionRequest)
            {
                Console.WriteLine($"mux v{Defaults.ProductVersion}");
                return 0;
            }

            if (args.Any(a => a == "--help" || a == "-h" || a == "-?" || a == "/?"))
            {
                PrintHelp();
                return 0;
            }

            string? configDirectoryOverride = GetConfigDirectoryOverride(args);
            using IDisposable configScope = SettingsLoader.PushConfigDirectoryOverride(configDirectoryOverride);

            bool isNonInteractiveCommand = args.Any(a =>
                a == "--print"
                || a == "-p"
                || a == "print"
                || a == "probe"
                || a == "endpoint");

            if (!isNonInteractiveCommand && !Console.IsOutputRedirected)
            {
                Console.WriteLine($"mux v{Defaults.ProductVersion} - AI agent for local and remote LLMs");
            }

            return Dispatch(args);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Prints the branded help text matching the mux specification.
        /// </summary>
        private static void PrintHelp()
        {
            string help = $@"mux v{Defaults.ProductVersion} - AI agent for local and remote LLMs
(c)2026 Joel Christner - MIT License

USAGE:
    mux [prompt]                         Interactive REPL (default)
    mux [OPTIONS] [prompt]               Interactive with overrides
    mux --print [OPTIONS] <prompt>       Single-shot mode
    echo ""prompt"" | mux --print          Read prompt from stdin
    mux probe [OPTIONS]                  Validate config and backend access
    mux endpoint <list|ls|show> [OPTIONS] Inspect configured endpoints

OPTIONS:
    -h, --help, /?                       Show this help message and exit
        --version, /version, -v          Show version and exit
    -p, --print                          Single-shot: process prompt, print result, exit
        --output-format <format>         text, json, or jsonl depending on the command
        --config-dir <path>              Override active config directory

  Endpoint / Model:
    -e, --endpoint <name>                Named endpoint from active config endpoints.json
    -m, --model <name>                   Override model name
        --base-url <url>                 Override base URL
        --adapter-type <type>            Adapter: ollama, openai, vllm, openai-compatible
        --temperature <float>            Override temperature (0.0 - 2.0)
        --max-tokens <int>               Override max output tokens
        --compaction-strategy <mode>     summary or trim

  Approval / Safety:
        --yolo                           Auto-approve all tool calls
        --approval-policy <policy>       interactive: ask, auto, or deny | print/probe: auto or deny

  Execution:
    -w, --working-directory <path>       Set working directory for tool execution
        --system-prompt <path>           Path to system prompt file
        --output-last-message <path>     Write only the final assistant response text to a file
        --no-mcp                         Interactive only: skip MCP server initialization
        --ignore-cert-errors, --insecure Disable TLS certificate validation for mux-owned network requests
    -v, --verbose                        Emit detailed progress to stderr

PROBE:
    mux probe --output-format json       Machine-readable health check
    mux probe -e openai-prod             Validate a specific configured endpoint
    mux probe --require-tools            Fail if the selected endpoint cannot use tools

ENDPOINTS:
    mux endpoint list --output-format json
    mux endpoint ls --output-format json
    mux endpoint show openai-prod --output-format json

EXAMPLES:
    mux                                  Start interactive session (default endpoint)
    mux --endpoint ollama-qwen           Start with specific endpoint
    mux -p --yolo ""read README.md""       Single-shot with auto-approval
    mux print --output-format jsonl --yolo ""read README.md""
    mux print --output-last-message out.txt --yolo ""read README.md""
    mux -p -e openai-gpt4 ""explain x""   Single-shot with OpenAI
    mux probe --output-format json
    mux endpoint list --output-format json
    mux endpoint ls --output-format json
    mux --base-url http://localhost:11434/v1 --model llama3.1:70b
                                         Ad-hoc endpoint, no config needed

CONFIG:
    Active config dir defaults to ~/.mux/
    Override with MUX_CONFIG_DIR or --config-dir for isolated runs and orchestration

    See CONFIG.md for full configuration reference.
    See USAGE.md for detailed usage examples.";

            Console.WriteLine(help);
        }

        private static string? GetConfigDirectoryOverride(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--config-dir", StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length ? args[i + 1] : null;
                }

                const string Prefix = "--config-dir=";
                if (arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return arg.Substring(Prefix.Length);
                }
            }

            return null;
        }

        private static int Dispatch(string[] args)
        {
            try
            {
                if (args.Length > 0 && string.Equals(args[0], "print", StringComparison.OrdinalIgnoreCase))
                {
                    string[] commandArgs = args.Skip(1).ToArray();
                    PrintSettings settings = CliArgumentParser.ParsePrint(commandArgs);
                    return new PrintCommand()
                        .ExecuteAsync(new CommandContext("print", args), settings, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                if (args.Any(a => string.Equals(a, "--print", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(a, "-p", StringComparison.OrdinalIgnoreCase)))
                {
                    PrintSettings settings = CliArgumentParser.ParsePrint(args);
                    settings.Print = true;
                    return new PrintCommand()
                        .ExecuteAsync(new CommandContext("print", args), settings, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                if (args.Length > 0 && string.Equals(args[0], "probe", StringComparison.OrdinalIgnoreCase))
                {
                    string[] commandArgs = args.Skip(1).ToArray();
                    ProbeSettings settings = CliArgumentParser.ParseProbe(commandArgs);
                    return new ProbeCommand()
                        .ExecuteAsync(new CommandContext("probe", args), settings, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                if (args.Length > 0 && string.Equals(args[0], "endpoint", StringComparison.OrdinalIgnoreCase))
                {
                    string[] commandArgs = args.Skip(1).ToArray();
                    EndpointSettings settings = CliArgumentParser.ParseEndpoint(commandArgs);
                    return new EndpointCommand()
                        .ExecuteAsync(new CommandContext("endpoint", args), settings, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                }

                // Default path: the TUIKit-hosted interactive shell.
                return RunInteractive(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>
        /// Resolves runtime configuration and runs the TUIKit interactive shell to completion.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>The process exit code.</returns>
        private static int RunInteractive(string[] args)
        {
            InteractiveSettings settings = CliArgumentParser.ParseInteractive(args);
            ResolvedRuntime runtime = CommandRuntimeResolver.ResolveRuntime(
                settings, "interactive", supportsMcp: true, allowAskApproval: true);

            // The default "ask" policy maps to AutoSafe: read-only tools run automatically and mutating
            // tools escalate to the interactive approval modal (wired below). --yolo / --approval-policy
            // auto auto-approves everything; deny blocks all tools.
            ApprovalPolicyEnum effectivePolicy = runtime.ApprovalPolicy;
            if (effectivePolicy == ApprovalPolicyEnum.Ask)
            {
                effectivePolicy = ApprovalPolicyEnum.AutoSafe;
            }

            AgentLoopOptions template = BuildInteractiveTemplate(runtime, settings, effectivePolicy, null);
            JobManager jobManager = JobManager.CreateForAgentLoop(template, runtime.MuxSettings.MaxConcurrency);

            // The built-in tool set fills {ToolDescriptions}; MCP tools are appended on top of the resulting
            // base prompt as servers connect. `basePrompt`/`baseCompaction` hold the MCP-free prompt so the
            // MCP section can be rebuilt without disturbing the model's core instructions.
            List<ToolDefinition> builtInTools = new BuiltInToolRegistry(runtime.MuxSettings).GetToolDefinitions();
            string basePrompt = runtime.SystemPrompt;
            string baseCompaction = runtime.CompactionSystemPrompt;
            object promptSync = new object();
            McpRuntime? mcpRuntime = null;
            SkillRuntime? skillRuntime = null;

            // Re-binds the live MCP tools and the skills runtime (callable tools + prompt awareness) onto the
            // template. Runs at startup, whenever the MCP tool set or the skill set changes, and on profile
            // switch. The template is read per job run, so updates apply to the next submitted turn.
            void ApplyTemplate()
            {
                lock (promptSync)
                {
                    List<ToolDefinition> mcpTools = mcpRuntime?.CurrentTools ?? new List<ToolDefinition>();
                    Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>? executor =
                        mcpRuntime != null ? mcpRuntime.ExecuteToolAsync : null;
                    ExternalToolsBinder.Apply(template, basePrompt, baseCompaction, mcpTools, executor, skillRuntime, builtInTools.Count);
                }
            }

            mcpRuntime = new McpRuntime(SettingsLoader.LoadMcpServers, ApplyTemplate, TimeSpan.FromSeconds(30));

            if (runtime.MuxSettings.SkillsEnabled)
            {
                string skillsDirectory = runtime.MuxSettings.SkillsDirectory ?? SettingsLoader.GetSkillsDirectory();
                skillRuntime = new SkillRuntime(
                    skillsDirectory,
                    SettingsLoader.LoadSkillIndex,
                    ApplyTemplate,
                    TimeSpan.FromSeconds(runtime.MuxSettings.SkillRefreshIntervalSeconds));
            }

            try
            {
                string title = string.IsNullOrWhiteSpace(runtime.Endpoint.Model)
                    ? runtime.Endpoint.Name
                    : $"{runtime.Endpoint.Name} · {runtime.Endpoint.Model}";

                SessionStore sessionStore = new SessionStore();

                // Baseline bind (wires the executor and leaves the prompt at its MCP-free base until the
                // first MCP discovery completes).
                ApplyTemplate();

                using MuxTuiApp app = new MuxTuiApp(
                    new ConsoleBackend(),
                    jobManager,
                    title,
                    effectivePolicy,
                    sessionStore,
                    runtime.Endpoint.Name,
                    runtime.Endpoint.Model,
                    onEndpointSelected: (EndpointConfig endpoint) => template.Endpoint = endpoint,
                    onPromptProfileSelected: (PromptProfile profile) =>
                    {
                        // Re-substitute placeholders for the current endpoint's tool support to form the new
                        // base prompt, then re-append the MCP section so the selected profile keeps MCP
                        // awareness.
                        bool toolsEnabled = template.Endpoint.Quirks?.SupportsTools ?? true;
                        (string systemPrompt, string compactionPrompt) = CommandRuntimeResolver.ResolveProfilePrompts(
                            profile, toolsEnabled, runtime.WorkingDirectory, builtInTools);
                        lock (promptSync)
                        {
                            basePrompt = systemPrompt;
                            baseCompaction = compactionPrompt;
                        }

                        ApplyTemplate();
                    },
                    showSplash: true,
                    showBoundaries: runtime.MuxSettings.ShowBoundaryLines,
                    mcpRuntime: mcpRuntime,
                    skillRuntime: skillRuntime);

                // Route escalated tool approvals to the shell's modal. The template is captured by
                // CreateForAgentLoop and read per job run, so setting this before the run loop starts
                // (jobs only run after the user submits) takes effect for every job.
                template.PromptUserFunc = (ToolCall toolCall) => app.RequestApprovalAsync(toolCall);

                // Connect MCP servers and discover skills in the background.
                mcpRuntime.Start();
                skillRuntime?.Start();

                using CancellationTokenSource cts = new CancellationTokenSource();
                app.RunAsync(cts.Token).GetAwaiter().GetResult();
                return 0;
            }
            finally
            {
                skillRuntime?.Dispose();
                mcpRuntime.Dispose();
                jobManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Builds the baseline agent-loop options cloned for each interactive job.
        /// </summary>
        private static AgentLoopOptions BuildInteractiveTemplate(
            ResolvedRuntime runtime,
            InteractiveSettings settings,
            ApprovalPolicyEnum approvalPolicy,
            Func<ToolCall, Task<string>>? promptFunc)
        {
            return new AgentLoopOptions(runtime.Endpoint)
            {
                MuxSettings = runtime.MuxSettings,
                IgnoreCertErrors = runtime.MuxSettings.IgnoreCertErrors,
                SystemPrompt = runtime.SystemPrompt,
                CompactionSystemPrompt = runtime.CompactionSystemPrompt,
                ApprovalPolicy = approvalPolicy,
                PromptUserFunc = promptFunc,
                WorkingDirectory = runtime.WorkingDirectory,
                MaxIterations = runtime.MaxAgentIterations,
                TokenEstimationRatio = runtime.MuxSettings.TokenEstimationRatio,
                ContextWindowSafetyMarginPercent = runtime.MuxSettings.ContextWindowSafetyMarginPercent,
                AutoCompactEnabled = runtime.MuxSettings.AutoCompactEnabled,
                ContextWarningThresholdPercent = runtime.MuxSettings.ContextWarningThresholdPercent,
                CompactionStrategy = runtime.MuxSettings.CompactionStrategy,
                CompactionPreserveTurns = runtime.MuxSettings.CompactionPreserveTurns,
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
        }

        #endregion
    }
}
