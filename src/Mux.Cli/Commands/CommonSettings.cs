namespace Mux.Cli.Commands
{
    using System.ComponentModel;
    using Spectre.Console.Cli;

    /// <summary>
    /// Shared CLI settings inherited by all mux commands.
    /// </summary>
    public class CommonSettings : CommandSettings
    {
        #region Public-Members

        /// <summary>
        /// The named endpoint to use from endpoints.json.
        /// </summary>
        [Description("Named endpoint from endpoints.json.")]
        [CommandOption("-e|--endpoint")]
        public string? Endpoint { get; set; }

        /// <summary>
        /// Override the model identifier.
        /// </summary>
        [Description("Model identifier override.")]
        [CommandOption("-m|--model")]
        public string? Model { get; set; }

        /// <summary>
        /// Override the base URL of the LLM endpoint.
        /// </summary>
        [Description("Base URL of the LLM endpoint.")]
        [CommandOption("--base-url")]
        public string? BaseUrl { get; set; }

        /// <summary>
        /// Override the adapter type (e.g. Ollama, OpenAi, Vllm, OpenAiCompatible).
        /// </summary>
        [Description("Adapter type (Ollama, OpenAi, Vllm, OpenAiCompatible).")]
        [CommandOption("--adapter-type")]
        public string? AdapterType { get; set; }

        /// <summary>
        /// Override the sampling temperature.
        /// </summary>
        [Description("Sampling temperature (0.0 - 2.0).")]
        [CommandOption("--temperature")]
        public double? Temperature { get; set; }

        /// <summary>
        /// Override the maximum number of tokens to generate.
        /// </summary>
        [Description("Maximum tokens to generate per response.")]
        [CommandOption("--max-tokens")]
        public int? MaxTokens { get; set; }

        /// <summary>
        /// The working directory for tool execution.
        /// </summary>
        [Description("Working directory for tool execution.")]
        [CommandOption("-w|--working-directory")]
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Path to a custom system prompt file.
        /// </summary>
        [Description("Path to a custom system prompt file.")]
        [CommandOption("--system-prompt")]
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Enable YOLO mode: auto-approve all tool calls without prompting.
        /// </summary>
        [Description("Auto-approve all tool calls without prompting.")]
        [CommandOption("--yolo")]
        public bool Yolo { get; set; }

        /// <summary>
        /// The approval policy for tool calls (Ask, AutoApprove, Deny).
        /// </summary>
        [Description("Approval policy for tool calls (Ask, AutoApprove, Deny).")]
        [CommandOption("--approval-policy")]
        public string? ApprovalPolicy { get; set; }

        /// <summary>
        /// Enable verbose diagnostic output.
        /// </summary>
        [Description("Enable verbose diagnostic output.")]
        [CommandOption("-v|--verbose")]
        public bool Verbose { get; set; }

        /// <summary>
        /// Disable MCP server connections.
        /// </summary>
        [Description("Disable MCP server connections.")]
        [CommandOption("--no-mcp")]
        public bool NoMcp { get; set; }

        /// <summary>
        /// Output format for command results.
        /// </summary>
        [Description("Output format: text, json, or jsonl depending on the command.")]
        [CommandOption("--output-format")]
        public string? OutputFormat { get; set; }

        #endregion
    }
}
