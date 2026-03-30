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
