namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Global settings for mux.
    /// </summary>
    public class MuxSettings
    {
        #region Private-Members

        private string? _SystemPromptPath = null;
        private string _DefaultApprovalPolicy = "ask";
        private int _ToolTimeoutMs = 30000;
        private int _ProcessTimeoutMs = 120000;
        private int _ContextWindowSafetyMarginPercent = 15;
        private double _TokenEstimationRatio = 3.5;
        private bool _AutoCompactEnabled = true;
        private int _ContextWarningThresholdPercent = 80;
        private string _CompactionStrategy = "summary";
        private int _CompactionPreserveTurns = 3;
        private int _MaxAgentIterations = 50;
        private int _MaxConcurrency = 3;
        private string _DefaultEnqueueBehavior = "ask";
        private bool _IgnoreCertErrors = false;
        private bool _ShowBoundaryLines = false;
        private bool _SkillsEnabled = true;
        private int _SkillRefreshIntervalSeconds = 30;
        private string? _SkillsDirectory = null;
        private bool _TaskPlanningEnabled = true;
        private bool _TaskParallelismEnabled = false;
        private ExternalSearchSettings _ExternalSearch = new ExternalSearchSettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxSettings"/> class with default values.
        /// </summary>
        public MuxSettings()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The file path to the system prompt, or null to use the built-in default.
        /// </summary>
        [JsonPropertyName("systemPromptPath")]
        public string? SystemPromptPath
        {
            get => _SystemPromptPath;
            set => _SystemPromptPath = value;
        }

        /// <summary>
        /// The default approval policy for tool execution. Common values: "ask", "auto", "deny".
        /// </summary>
        [JsonPropertyName("defaultApprovalPolicy")]
        public string DefaultApprovalPolicy
        {
            get => _DefaultApprovalPolicy;
            set => _DefaultApprovalPolicy = value ?? "ask";
        }

        /// <summary>
        /// The timeout in milliseconds for individual tool executions.
        /// Clamped to the range 1000-300000.
        /// </summary>
        [JsonPropertyName("toolTimeoutMs")]
        public int ToolTimeoutMs
        {
            get => _ToolTimeoutMs;
            set => _ToolTimeoutMs = Math.Clamp(value, 1000, 300000);
        }

        /// <summary>
        /// The timeout in milliseconds for spawned processes.
        /// Clamped to the range 1000-600000.
        /// </summary>
        [JsonPropertyName("processTimeoutMs")]
        public int ProcessTimeoutMs
        {
            get => _ProcessTimeoutMs;
            set => _ProcessTimeoutMs = Math.Clamp(value, 1000, 600000);
        }

        /// <summary>
        /// The percentage of the context window to reserve as a safety margin.
        /// Clamped to the range 5-50.
        /// </summary>
        [JsonPropertyName("contextWindowSafetyMarginPercent")]
        public int ContextWindowSafetyMarginPercent
        {
            get => _ContextWindowSafetyMarginPercent;
            set => _ContextWindowSafetyMarginPercent = Math.Clamp(value, 5, 50);
        }

        /// <summary>
        /// The estimated ratio of characters to tokens used for quick token estimation.
        /// Clamped to the range 2.0-6.0.
        /// </summary>
        [JsonPropertyName("tokenEstimationRatio")]
        public double TokenEstimationRatio
        {
            get => _TokenEstimationRatio;
            set => _TokenEstimationRatio = Math.Clamp(value, 2.0, 6.0);
        }

        /// <summary>
        /// Whether mux should automatically compact persisted conversation history before a run
        /// when the estimated prompt would exceed the usable context budget.
        /// </summary>
        [JsonPropertyName("autoCompactEnabled")]
        public bool AutoCompactEnabled
        {
            get => _AutoCompactEnabled;
            set => _AutoCompactEnabled = value;
        }

        /// <summary>
        /// The percentage of the usable input budget at which mux starts warning that context
        /// pressure is getting high. Clamped to the range 50-95.
        /// </summary>
        [JsonPropertyName("contextWarningThresholdPercent")]
        public int ContextWarningThresholdPercent
        {
            get => _ContextWarningThresholdPercent;
            set => _ContextWarningThresholdPercent = Math.Clamp(value, 50, 95);
        }

        /// <summary>
        /// The automatic/manual compaction strategy. Supported values are "summary" and "trim".
        /// Any other value falls back to "summary".
        /// </summary>
        [JsonPropertyName("compactionStrategy")]
        public string CompactionStrategy
        {
            get => _CompactionStrategy;
            set
            {
                _CompactionStrategy = TryNormalizeCompactionStrategy(value, out string normalized)
                    ? normalized
                    : "summary";
            }
        }

        /// <summary>
        /// The number of recent user-led turns to preserve during compaction.
        /// Clamped to the range 1-10.
        /// </summary>
        [JsonPropertyName("compactionPreserveTurns")]
        public int CompactionPreserveTurns
        {
            get => _CompactionPreserveTurns;
            set => _CompactionPreserveTurns = Math.Clamp(value, 1, 10);
        }

        /// <summary>
        /// The maximum number of agent loop iterations before forcing a stop.
        /// Clamped to the range 1-100.
        /// </summary>
        [JsonPropertyName("maxAgentIterations")]
        public int MaxAgentIterations
        {
            get => _MaxAgentIterations;
            set => _MaxAgentIterations = Math.Clamp(value, 1, 100);
        }

        /// <summary>
        /// The maximum number of jobs that may run concurrently.
        /// Clamped to the range 1-32. Defaults to 3.
        /// </summary>
        [JsonPropertyName("maxConcurrency")]
        public int MaxConcurrency
        {
            get => _MaxConcurrency;
            set => _MaxConcurrency = Math.Clamp(value, 1, 32);
        }

        /// <summary>
        /// The default behavior when submitting while another job is active.
        /// Supported values are "ask", "run_now", "queue_after", and "add_to_focused".
        /// </summary>
        [JsonPropertyName("defaultEnqueueBehavior")]
        public string DefaultEnqueueBehavior
        {
            get => _DefaultEnqueueBehavior;
            set
            {
                _DefaultEnqueueBehavior = TryNormalizeDefaultEnqueueBehavior(value, out string normalized)
                    ? normalized
                    : "ask";
            }
        }

        /// <summary>
        /// Whether mux-owned network requests should bypass TLS certificate validation.
        /// Intended for enterprise networks that intercept TLS with private certificates.
        /// </summary>
        [JsonPropertyName("ignoreCertErrors")]
        public bool IgnoreCertErrors
        {
            get => _IgnoreCertErrors;
            set => _IgnoreCertErrors = value;
        }

        /// <summary>
        /// Whether the interactive shell draws dark-grey boundary lines: a horizontal rule above the
        /// prompt input, a horizontal rule above the queued-messages strip, and a vertical rule in the
        /// gutter to the left of the sidebar. Defaults to false (off). Toggle live with <c>/borders</c>.
        /// </summary>
        [JsonPropertyName("showBoundaryLines")]
        public bool ShowBoundaryLines
        {
            get => _ShowBoundaryLines;
            set => _ShowBoundaryLines = value;
        }

        /// <summary>
        /// Whether user-authored skills are loaded and exposed to the model in the interactive shell.
        /// Defaults to true.
        /// </summary>
        [JsonPropertyName("skillsEnabled")]
        public bool SkillsEnabled
        {
            get => _SkillsEnabled;
            set => _SkillsEnabled = value;
        }

        /// <summary>
        /// How often the interactive shell re-scans the skills directory for changes, in seconds. Clamped to
        /// a minimum of 5. Defaults to 30.
        /// </summary>
        [JsonPropertyName("skillRefreshIntervalSeconds")]
        public int SkillRefreshIntervalSeconds
        {
            get => _SkillRefreshIntervalSeconds;
            set => _SkillRefreshIntervalSeconds = Math.Max(5, value);
        }

        /// <summary>
        /// An optional override for the skills directory, letting a team point mux at a shared, version-
        /// controlled library instead of the default <c>~/.mux/skills</c>. Null uses the default.
        /// </summary>
        [JsonPropertyName("skillsDirectory")]
        public string? SkillsDirectory
        {
            get => _SkillsDirectory;
            set => _SkillsDirectory = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Whether the model may decompose a job into a tracked plan of background tasks and advance
        /// them with the <c>plan_tasks</c> and <c>update_task</c> tools. When true, the two task tools
        /// are offered to the model and the system prompt teaches when to plan; when false, neither the
        /// tools nor the guidance are surfaced. Defaults to true.
        /// </summary>
        [JsonPropertyName("taskPlanningEnabled")]
        public bool TaskPlanningEnabled
        {
            get => _TaskPlanningEnabled;
            set => _TaskPlanningEnabled = value;
        }

        /// <summary>
        /// Whether dependency-ready tasks in an orchestrated plan may be dispatched as their own
        /// concurrent jobs, subject to <see cref="MaxConcurrency"/> and the shared workspace write
        /// lease. Has no effect unless <see cref="TaskPlanningEnabled"/> is also true. Defaults to
        /// false, so task planning tracks work by default and only fans out to parallel jobs when the
        /// operator opts in.
        /// </summary>
        [JsonPropertyName("taskParallelismEnabled")]
        public bool TaskParallelismEnabled
        {
            get => _TaskParallelismEnabled;
            set => _TaskParallelismEnabled = value;
        }

        /// <summary>
        /// External web-search provider configuration.
        /// </summary>
        [JsonPropertyName("externalSearch")]
        public ExternalSearchSettings ExternalSearch
        {
            get => _ExternalSearch;
            set => _ExternalSearch = value ?? new ExternalSearchSettings();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolves the effective maximum agent iteration count for an endpoint.
        /// Endpoint-scoped values override the global default when set.
        /// </summary>
        /// <param name="endpoint">The endpoint whose override should be considered.</param>
        /// <returns>The effective clamped maximum agent iteration count.</returns>
        public int GetEffectiveMaxAgentIterations(EndpointConfig? endpoint)
        {
            return endpoint?.MaxAgentIterations ?? MaxAgentIterations;
        }

        /// <summary>
        /// Normalizes a compaction strategy string.
        /// </summary>
        /// <param name="value">The raw strategy value.</param>
        /// <param name="normalized">The normalized value when successful.</param>
        /// <returns>True if the input matched a supported strategy; otherwise false.</returns>
        public static bool TryNormalizeCompactionStrategy(string? value, out string normalized)
        {
            string candidate = (value ?? string.Empty).Trim().ToLowerInvariant();

            switch (candidate)
            {
                case "summary":
                    normalized = "summary";
                    return true;
                case "trim":
                    normalized = "trim";
                    return true;
                default:
                    normalized = "summary";
                    return false;
            }
        }

        /// <summary>
        /// Normalizes a default enqueue behavior string.
        /// </summary>
        /// <param name="value">The raw behavior value.</param>
        /// <param name="normalized">The normalized value when successful.</param>
        /// <returns>True if the input matched a supported behavior; otherwise false.</returns>
        public static bool TryNormalizeDefaultEnqueueBehavior(string? value, out string normalized)
        {
            string candidate = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");

            switch (candidate)
            {
                case "ask":
                    normalized = "ask";
                    return true;
                case "run_now":
                case "runnow":
                case "parallel":
                    normalized = "run_now";
                    return true;
                case "queue_after":
                case "queueafter":
                case "queue":
                    normalized = "queue_after";
                    return true;
                case "add_to_focused":
                case "addtofocused":
                case "focused":
                    normalized = "add_to_focused";
                    return true;
                default:
                    normalized = "ask";
                    return false;
            }
        }

        #endregion
    }
}
