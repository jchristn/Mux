namespace Mux.Cli.Commands
{
    using System.Collections.Generic;
    using System.ComponentModel;

    /// <summary>
    /// Shared CLI settings inherited by all mux commands.
    /// </summary>
    public class CommonSettings : CommandSettings
    {
        #region Public-Members

        /// <summary>
        /// Override the active config directory used for endpoints, settings, and prompts.
        /// </summary>
        [Description("Override the active config directory.")]
        [CommandOption("--config-dir")]
        public string? ConfigDir { get; set; }

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
        /// Override the reasoning effort level: off, minimal, low, medium, or high. "off" disables the
        /// reasoning field even when the endpoint sets a level.
        /// </summary>
        [Description("Reasoning effort: off, minimal, low, medium, high.")]
        [CommandOption("--effort")]
        public string? Effort { get; set; }

        /// <summary>
        /// Override the OpenAI reasoning_effort value sent to OpenAI-compatible backends.
        /// </summary>
        [Description("Override the OpenAI reasoning_effort value.")]
        [CommandOption("--effort-openai-value")]
        public string? EffortOpenAiValue { get; set; }

        /// <summary>
        /// Override the Gemini thinking-token budget (-1 dynamic, 0 off, or a positive budget).
        /// </summary>
        [Description("Override the Gemini thinking budget (-1..32768).")]
        [CommandOption("--effort-gemini-budget")]
        public int? EffortGeminiBudget { get; set; }

        /// <summary>
        /// Override the Ollama think value: low, medium, high, true, or false.
        /// </summary>
        [Description("Override the Ollama think value (low/medium/high/true/false).")]
        [CommandOption("--effort-ollama-think")]
        public string? EffortOllamaThink { get; set; }

        /// <summary>
        /// Surface the model's reasoning ("thinking") for this run, overriding the endpoint's setting.
        /// </summary>
        [Description("Surface the model's reasoning (thinking) for this run.")]
        [CommandOption("--show-thinking")]
        public bool ShowThinking { get; set; }

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
        /// Path to a JSON file, or inline JSON, describing MCP servers to load for this run.
        /// </summary>
        [Description("MCP server config as a file path or inline JSON; enables MCP in print mode.")]
        [CommandOption("--mcp-config")]
        public string? McpConfig { get; set; }

        /// <summary>
        /// Use only the servers from <see cref="McpConfig"/>, ignoring the config directory's mcp-servers.json.
        /// </summary>
        [Description("Use only --mcp-config servers, ignoring the config directory's mcp-servers.json.")]
        [CommandOption("--strict-mcp-config")]
        public bool StrictMcpConfig { get; set; }

        /// <summary>
        /// Output format for command results.
        /// </summary>
        [Description("Output format: text, json, or jsonl depending on the command.")]
        [CommandOption("--output-format")]
        public string? OutputFormat { get; set; }

        /// <summary>
        /// Input format for the prompt stream: text (default) or jsonl for multi-turn stdin records.
        /// </summary>
        [Description("Input format: text (default) or jsonl (multi-turn stdin records).")]
        [CommandOption("--input-format")]
        public string? InputFormat { get; set; }

        /// <summary>
        /// Override the compaction strategy used for automatic and manual history compaction.
        /// </summary>
        [Description("Compaction strategy (summary or trim).")]
        [CommandOption("--compaction-strategy")]
        public string? CompactionStrategy { get; set; }

        /// <summary>
        /// Override the maximum number of agent loop iterations (turns) for the run. Clamped to 1-100.
        /// </summary>
        [Description("Maximum agent loop iterations (turns) for the run.")]
        [CommandOption("--max-turns")]
        public int? MaxTurns { get; set; }

        /// <summary>
        /// Text appended to the resolved system prompt after all placeholder substitution.
        /// </summary>
        [Description("Text appended to the resolved system prompt.")]
        [CommandOption("--append-system-prompt")]
        public string? AppendSystemPrompt { get; set; }

        /// <summary>
        /// Ceiling on the estimated working-context tokens before the run stops with a budget_exceeded error.
        /// </summary>
        [Description("Estimated-token ceiling; stops the run with budget_exceeded when exceeded.")]
        [CommandOption("--max-token-budget")]
        public int? MaxTokenBudget { get; set; }

        /// <summary>
        /// Comma-separated tool-name glob patterns; when set, only matching tools may be used.
        /// </summary>
        [Description("Comma-separated tool-name globs; only matching tools are allowed.")]
        [CommandOption("--allow-tools")]
        public string? AllowTools { get; set; }

        /// <summary>
        /// Comma-separated tool-name glob patterns to deny; a deny match always wins.
        /// </summary>
        [Description("Comma-separated tool-name globs to deny (deny wins over allow).")]
        [CommandOption("--deny-tools")]
        public string? DenyTools { get; set; }

        /// <summary>
        /// Additional writable roots honored under the workspace-write sandbox. Repeatable.
        /// </summary>
        [Description("Additional writable root under the workspace-write sandbox (repeatable).")]
        [CommandOption("--add-dir")]
        public List<string> AddDir { get; set; } = new List<string>();

        /// <summary>
        /// Application-level confinement posture: none, read-only, or workspace-write.
        /// </summary>
        [Description("Confinement posture: none, read-only, or workspace-write.")]
        [CommandOption("--sandbox")]
        public string? Sandbox { get; set; }

        /// <summary>
        /// Disable TLS certificate validation for mux-owned network requests.
        /// </summary>
        [Description("Disable TLS certificate validation for mux-owned network requests.")]
        [CommandOption("--ignore-cert-errors|--insecure")]
        public bool IgnoreCertErrors { get; set; }

        #endregion
    }
}
