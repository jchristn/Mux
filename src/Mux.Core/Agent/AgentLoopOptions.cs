namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Configuration options for the <see cref="AgentLoop"/>.
    /// </summary>
    public class AgentLoopOptions
    {
        #region Private-Members

        private EndpointConfig _Endpoint;
        private List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private string _SystemPrompt = string.Empty;
        private ApprovalPolicyEnum _ApprovalPolicy = ApprovalPolicyEnum.Ask;
        private string _WorkingDirectory = Directory.GetCurrentDirectory();
        private int _MaxIterations = 25;
        private bool _Verbose = false;
        private string _CommandName = string.Empty;
        private string _ConfigDirectory = string.Empty;
        private string _EndpointSelectionSource = string.Empty;
        private List<string> _CliOverridesApplied = new List<string>();
        private bool _McpSupported = false;
        private bool _McpConfigured = false;
        private int _McpServerCount = 0;
        private int _BuiltInToolCount = 0;
        private int _EffectiveToolCount = 0;
        private List<ToolDefinition>? _AdditionalTools = null;
        private Func<ToolCall, Task<string>>? _PromptUserFunc = null;
        private Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>? _ExternalToolExecutor = null;
        private Action<int, int, string>? _OnRetry = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentLoopOptions"/> class.
        /// </summary>
        /// <param name="endpoint">The endpoint configuration to use for LLM requests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoint"/> is null.</exception>
        public AgentLoopOptions(EndpointConfig endpoint)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The LLM endpoint configuration. Required.
        /// </summary>
        public EndpointConfig Endpoint
        {
            get => _Endpoint;
            set => _Endpoint = value ?? throw new ArgumentNullException(nameof(Endpoint));
        }

        /// <summary>
        /// The existing conversation history to continue from. Defaults to an empty list.
        /// </summary>
        public List<ConversationMessage> ConversationHistory
        {
            get => _ConversationHistory;
            set => _ConversationHistory = value ?? new List<ConversationMessage>();
        }

        /// <summary>
        /// The system prompt to prepend to the conversation. Defaults to empty.
        /// </summary>
        public string SystemPrompt
        {
            get => _SystemPrompt;
            set => _SystemPrompt = value ?? string.Empty;
        }

        /// <summary>
        /// The approval policy for tool calls. Defaults to <see cref="ApprovalPolicyEnum.Ask"/>.
        /// </summary>
        public ApprovalPolicyEnum ApprovalPolicy
        {
            get => _ApprovalPolicy;
            set => _ApprovalPolicy = value;
        }

        /// <summary>
        /// The working directory for tool execution. Defaults to the current directory.
        /// </summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = value ?? Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// The maximum number of loop iterations before stopping. Clamped to the range 1-100. Defaults to 25.
        /// </summary>
        public int MaxIterations
        {
            get => _MaxIterations;
            set => _MaxIterations = Math.Clamp(value, 1, 100);
        }

        /// <summary>
        /// Whether to emit verbose diagnostic information. Defaults to false.
        /// </summary>
        public bool Verbose
        {
            get => _Verbose;
            set => _Verbose = value;
        }

        /// <summary>
        /// The high-level command mode executing the loop, such as print.
        /// </summary>
        public string CommandName
        {
            get => _CommandName;
            set => _CommandName = value ?? string.Empty;
        }

        /// <summary>
        /// The effective mux configuration directory used for this run.
        /// </summary>
        public string ConfigDirectory
        {
            get => _ConfigDirectory;
            set => _ConfigDirectory = value ?? string.Empty;
        }

        /// <summary>
        /// Describes how the endpoint was selected for this run.
        /// </summary>
        public string EndpointSelectionSource
        {
            get => _EndpointSelectionSource;
            set => _EndpointSelectionSource = value ?? string.Empty;
        }

        /// <summary>
        /// The CLI override categories applied to this run.
        /// </summary>
        public List<string> CliOverridesApplied
        {
            get => _CliOverridesApplied;
            set => _CliOverridesApplied = value ?? new List<string>();
        }

        /// <summary>
        /// Whether the command mode supports MCP integration.
        /// </summary>
        public bool McpSupported
        {
            get => _McpSupported;
            set => _McpSupported = value;
        }

        /// <summary>
        /// Whether MCP servers are configured in the active config directory.
        /// </summary>
        public bool McpConfigured
        {
            get => _McpConfigured;
            set => _McpConfigured = value;
        }

        /// <summary>
        /// The number of configured MCP servers.
        /// </summary>
        public int McpServerCount
        {
            get => _McpServerCount;
            set => _McpServerCount = value;
        }

        /// <summary>
        /// The number of built-in tools compiled into mux.
        /// </summary>
        public int BuiltInToolCount
        {
            get => _BuiltInToolCount;
            set => _BuiltInToolCount = value;
        }

        /// <summary>
        /// The number of tools effectively available to the model for this run.
        /// </summary>
        public int EffectiveToolCount
        {
            get => _EffectiveToolCount;
            set => _EffectiveToolCount = value;
        }

        /// <summary>
        /// Additional tool definitions (e.g. from MCP servers) to include alongside built-in tools. Nullable.
        /// </summary>
        public List<ToolDefinition>? AdditionalTools
        {
            get => _AdditionalTools;
            set => _AdditionalTools = value;
        }

        /// <summary>
        /// A callback that prompts the user for interactive approval of a tool call and returns their response. Nullable.
        /// </summary>
        public Func<ToolCall, Task<string>>? PromptUserFunc
        {
            get => _PromptUserFunc;
            set => _PromptUserFunc = value;
        }

        /// <summary>
        /// A callback for executing external (MCP) tools by name.
        /// Parameters: toolName, arguments, workingDirectory, cancellationToken.
        /// Nullable.
        /// </summary>
        public Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>? ExternalToolExecutor
        {
            get => _ExternalToolExecutor;
            set => _ExternalToolExecutor = value;
        }

        /// <summary>
        /// Optional callback invoked on each LLM connection retry attempt.
        /// Parameters: attempt number, max retries, error message.
        /// </summary>
        public Action<int, int, string>? OnRetry
        {
            get => _OnRetry;
            set => _OnRetry = value;
        }

        #endregion
    }
}
