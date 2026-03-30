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
    }
}
