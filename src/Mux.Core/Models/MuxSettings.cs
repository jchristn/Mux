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
        private int _MaxAgentIterations = 25;

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

        #endregion

        #region Public-Methods

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

        #endregion
    }
}
