namespace Mux.Cli
{
    using System;
    using System.Linq;
    using Mux.Cli.Commands;
    using Mux.Core.Settings;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// Entry point for the mux CLI application.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Application entry point. Configures and runs the Spectre.Console.Cli command pipeline.
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
                AnsiConsole.MarkupLine(
                    $"[bold cyan]{Defaults.ProductName}[/] [dim]v{Defaults.ProductVersion}[/] [dim]-[/] [dim]AI agent for local and remote LLMs[/]");

                string configDir = SettingsLoader.GetConfigDirectory();
                string endpointsPath = System.IO.Path.Combine(configDir, "endpoints.json");
                AnsiConsole.MarkupLine($"[dim]Using endpoints defined in: {Markup.Escape(endpointsPath)}[/]");
                AnsiConsole.WriteLine();
            }

            CommandApp app = new CommandApp();

            app.Configure((IConfigurator config) =>
            {
                config.SetApplicationName("mux");
                config.SetApplicationVersion(Defaults.ProductVersion);

                config.AddCommand<PrintCommand>("print")
                    .WithDescription("Run a single prompt and print the result to stdout.");

                config.AddCommand<ProbeCommand>("probe")
                    .WithDescription("Validate config, backend reachability, auth, and model access.");

                config.AddCommand<EndpointCommand>("endpoint")
                    .WithDescription("Inspect configured endpoints non-interactively.");
            });

            app.SetDefaultCommand<InteractiveCommand>();

            return app.Run(args);
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
    mux endpoint <list|show> [OPTIONS]   Inspect configured endpoints

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
    -v, --verbose                        Emit detailed progress to stderr

PROBE:
    mux probe --output-format json       Machine-readable health check
    mux probe -e openai-prod             Validate a specific configured endpoint
    mux probe --require-tools            Fail if the selected endpoint cannot use tools

ENDPOINTS:
    mux endpoint list --output-format json
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

        #endregion
    }
}
