namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;

    /// <summary>
    /// Event emitted when a mux run starts and effective runtime metadata is known.
    /// </summary>
    public class RunStartedEvent : AgentEvent
    {
        #region Private-Members

        private string _RunId = string.Empty;
        private string _EndpointName = string.Empty;
        private string _AdapterType = string.Empty;
        private string _BaseUrl = string.Empty;
        private string _Model = string.Empty;
        private string _ApprovalPolicy = string.Empty;
        private string _WorkingDirectory = string.Empty;
        private int _MaxIterations = 25;
        private bool _ToolsEnabled = true;
        private string _CommandName = string.Empty;
        private string _ConfigDirectory = string.Empty;
        private string _EndpointSelectionSource = string.Empty;
        private List<string> _CliOverridesApplied = new List<string>();
        private bool _McpSupported = false;
        private bool _McpConfigured = false;
        private int _McpServerCount = 0;
        private int _BuiltInToolCount = 0;
        private int _EffectiveToolCount = 0;
        private int _ContextWindow = 0;
        private int _ReservedOutputTokens = 0;
        private int _UsableInputLimit = 0;
        private int _WarningThresholdTokens = 0;
        private double _TokenEstimationRatio = 0;
        private string _CompactionStrategy = "summary";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="RunStartedEvent"/> class.
        /// </summary>
        public RunStartedEvent()
        {
            EventType = AgentEventTypeEnum.RunStarted;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Correlation identifier for the run.
        /// </summary>
        public string RunId
        {
            get => _RunId;
            set => _RunId = value ?? throw new ArgumentNullException(nameof(RunId));
        }

        /// <summary>
        /// Effective endpoint name.
        /// </summary>
        public string EndpointName
        {
            get => _EndpointName;
            set => _EndpointName = value ?? string.Empty;
        }

        /// <summary>
        /// Effective adapter type.
        /// </summary>
        public string AdapterType
        {
            get => _AdapterType;
            set => _AdapterType = value ?? string.Empty;
        }

        /// <summary>
        /// Effective base URL.
        /// </summary>
        public string BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = value ?? string.Empty;
        }

        /// <summary>
        /// Effective model identifier.
        /// </summary>
        public string Model
        {
            get => _Model;
            set => _Model = value ?? string.Empty;
        }

        /// <summary>
        /// Effective approval policy.
        /// </summary>
        public string ApprovalPolicy
        {
            get => _ApprovalPolicy;
            set => _ApprovalPolicy = value ?? string.Empty;
        }

        /// <summary>
        /// Working directory used for tool execution.
        /// </summary>
        public string WorkingDirectory
        {
            get => _WorkingDirectory;
            set => _WorkingDirectory = value ?? string.Empty;
        }

        /// <summary>
        /// Maximum agent iterations for the run.
        /// </summary>
        public int MaxIterations
        {
            get => _MaxIterations;
            set => _MaxIterations = value;
        }

        /// <summary>
        /// Whether tool calling is enabled for the endpoint.
        /// </summary>
        public bool ToolsEnabled
        {
            get => _ToolsEnabled;
            set => _ToolsEnabled = value;
        }

        /// <summary>
        /// The command mode executing the run, such as print.
        /// </summary>
        public string CommandName
        {
            get => _CommandName;
            set => _CommandName = value ?? string.Empty;
        }

        /// <summary>
        /// The effective mux configuration directory.
        /// </summary>
        public string ConfigDirectory
        {
            get => _ConfigDirectory;
            set => _ConfigDirectory = value ?? string.Empty;
        }

        /// <summary>
        /// How mux selected the effective endpoint.
        /// </summary>
        public string EndpointSelectionSource
        {
            get => _EndpointSelectionSource;
            set => _EndpointSelectionSource = value ?? string.Empty;
        }

        /// <summary>
        /// The CLI override categories applied to the run.
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
        /// Number of configured MCP servers in the active config directory.
        /// </summary>
        public int McpServerCount
        {
            get => _McpServerCount;
            set => _McpServerCount = value;
        }

        /// <summary>
        /// Number of built-in tools compiled into mux.
        /// </summary>
        public int BuiltInToolCount
        {
            get => _BuiltInToolCount;
            set => _BuiltInToolCount = value;
        }

        /// <summary>
        /// Number of tools effectively available to the model for this run.
        /// </summary>
        public int EffectiveToolCount
        {
            get => _EffectiveToolCount;
            set => _EffectiveToolCount = value;
        }

        /// <summary>
        /// Total configured context window for the selected model endpoint.
        /// </summary>
        public int ContextWindow
        {
            get => _ContextWindow;
            set => _ContextWindow = value;
        }

        /// <summary>
        /// Tokens reserved for the model's response generation.
        /// </summary>
        public int ReservedOutputTokens
        {
            get => _ReservedOutputTokens;
            set => _ReservedOutputTokens = value;
        }

        /// <summary>
        /// Estimated usable input budget after reservations.
        /// </summary>
        public int UsableInputLimit
        {
            get => _UsableInputLimit;
            set => _UsableInputLimit = value;
        }

        /// <summary>
        /// Warning threshold in tokens for this run.
        /// </summary>
        public int WarningThresholdTokens
        {
            get => _WarningThresholdTokens;
            set => _WarningThresholdTokens = value;
        }

        /// <summary>
        /// Approximate character-to-token ratio used for budget estimation.
        /// </summary>
        public double TokenEstimationRatio
        {
            get => _TokenEstimationRatio;
            set => _TokenEstimationRatio = value;
        }

        /// <summary>
        /// Effective compaction strategy for the run.
        /// </summary>
        public string CompactionStrategy
        {
            get => _CompactionStrategy;
            set => _CompactionStrategy = value ?? "summary";
        }

        #endregion
    }
}
