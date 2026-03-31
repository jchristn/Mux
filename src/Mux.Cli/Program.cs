namespace Mux.Cli
{
    using System;
    using System.IO;
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

            // Handle --version / /version early
            // Note: -v is also --verbose, so only treat bare "mux -v" as version
            bool isVersionRequest = args.Any(a => a == "--version" || a == "/version")
                || (args.Length == 1 && args[0] == "-v");
            if (isVersionRequest)
            {
                Console.WriteLine($"mux v{Defaults.ProductVersion}");
                return 0;
            }

            // Handle --help / -h / -? / /? early with custom output
            if (args.Any(a => a == "--help" || a == "-h" || a == "-?" || a == "/?"))
            {
                PrintHelp();
                return 0;
            }

            // Print banner for interactive usage (not when piping)
            bool isPrintMode = args.Any(a => a == "--print" || a == "-p" || a == "print");
            if (!isPrintMode)
            {
                AnsiConsole.MarkupLine(
                    $"[bold cyan]{Defaults.ProductName}[/] [dim]v{Defaults.ProductVersion}[/] — AI agent for local and remote LLMs");

                string configDir = Mux.Core.Settings.SettingsLoader.GetConfigDirectory();
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
            string help = $@"mux v{Defaults.ProductVersion} — AI agent for local and remote LLMs
(c)2026 Joel Christner — MIT License

USAGE:
    mux [prompt]                         Interactive REPL (default)
    mux [OPTIONS] [prompt]               Interactive with overrides
    mux --print [OPTIONS] <prompt>       Single-shot mode
    echo ""prompt"" | mux --print          Read prompt from stdin

OPTIONS:
    -h, --help, /?                       Show this help message and exit
        --version, /version, -v          Show version and exit
    -p, --print                          Single-shot: process prompt, print result, exit

  Endpoint / Model:
    -e, --endpoint <name>                Named endpoint from ~/.mux/endpoints.json
    -m, --model <name>                   Override model name
        --base-url <url>                 Override base URL
        --adapter-type <type>            Adapter: ollama, openai, vllm, openai-compatible
        --temperature <float>            Override temperature (0.0 - 2.0)
        --max-tokens <int>               Override max output tokens

  Approval / Safety:
        --yolo                           Auto-approve all tool calls
        --approval-policy <policy>       ask, auto, or deny (default: ask if TTY, deny otherwise)

  Execution:
    -w, --working-directory <path>       Set working directory for tool execution
        --system-prompt <path>           Path to system prompt file
        --no-mcp                         Skip MCP server initialization
    -v, --verbose                        Emit detailed progress to stderr

INTERACTIVE COMMANDS:
    /model [name]      List or switch endpoints
    /tools             List available tools (built-in + MCP)
    /mcp list|add|remove   Manage MCP servers
    /clear             Reset conversation history
    /system [text]     View or set system prompt
    /help, /?          Show interactive commands
    /exit, /quit       Exit mux

INPUT:
    Enter              Submit input
    Shift+Enter        Insert newline (multi-line input)
    Ctrl+Enter         Insert newline (multi-line input)
    Ctrl+C             Cancel generation / clear input
    Ctrl+C x2          Exit mux

EXAMPLES:
    mux                                  Start interactive session (default endpoint)
    mux --endpoint ollama-qwen           Start with specific endpoint
    mux -p --yolo ""read README.md""       Single-shot with auto-approval
    mux -p -e openai-gpt4 ""explain x""   Single-shot with OpenAI
    mux --base-url http://localhost:11434/v1 --model llama3.1:70b
                                         Ad-hoc endpoint, no config needed

CONFIG:
    ~/.mux/endpoints.json                Model runner endpoints (use ""headers"" dict for auth)
    ~/.mux/mcp-servers.json              MCP tool servers
    ~/.mux/settings.json                 Global settings
    ~/.mux/system-prompt.md              Custom system prompt (optional)

    See CONFIG.md for full configuration reference.
    See USAGE.md for detailed usage examples.";

            Console.WriteLine(help);
        }

        #endregion
    }
}
