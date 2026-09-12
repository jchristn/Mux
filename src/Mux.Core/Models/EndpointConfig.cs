namespace Mux.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Mux.Core.Enums;

    /// <summary>
    /// Defines a single LLM endpoint configuration.
    /// </summary>
    public class EndpointConfig
    {
        #region Private-Members

        private string _Name = string.Empty;
        private AdapterTypeEnum _AdapterType = AdapterTypeEnum.Ollama;
        private string _BaseUrl = string.Empty;
        private string _Model = string.Empty;
        private bool _IsDefault = false;
        private int _MaxTokens = 8192;
        private double _Temperature = 0.1;
        private int _ContextWindow = 32768;
        private int _TimeoutMs = 120000;
        private Dictionary<string, string> _Headers = new Dictionary<string, string>();
        private bool _AutoApproveTools = false;
        private int? _MaxAgentIterations = null;
        private BackendQuirks? _Quirks = null;
        private ReasoningEffortConfig? _ReasoningEffort = null;
        private bool _ShowThinking = false;
        private string? _ApiKey = null;
        private string? _Region = null;
        private string? _Project = null;
        private string? _ApiVersion = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointConfig"/> class with default values.
        /// </summary>
        public EndpointConfig()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The unique name of this endpoint.
        /// </summary>
        [JsonPropertyName("name")]
        public string Name
        {
            get => _Name;
            set => _Name = value ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// The adapter type used to communicate with this endpoint.
        /// </summary>
        [JsonPropertyName("adapterType")]
        public AdapterTypeEnum AdapterType
        {
            get => _AdapterType;
            set => _AdapterType = value;
        }

        /// <summary>
        /// The base URL of the endpoint API.
        /// </summary>
        [JsonPropertyName("baseUrl")]
        public string BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = value ?? throw new ArgumentNullException(nameof(BaseUrl));
        }

        /// <summary>
        /// The model identifier to use at this endpoint.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model
        {
            get => _Model;
            set => _Model = value ?? throw new ArgumentNullException(nameof(Model));
        }

        /// <summary>
        /// Whether this endpoint is the default endpoint.
        /// </summary>
        [JsonPropertyName("isDefault")]
        public bool IsDefault
        {
            get => _IsDefault;
            set => _IsDefault = value;
        }

        /// <summary>
        /// The maximum number of tokens the model may generate per response.
        /// Clamped to the range 1024-131072.
        /// </summary>
        [JsonPropertyName("maxTokens")]
        public int MaxTokens
        {
            get => _MaxTokens;
            set => _MaxTokens = Math.Clamp(value, 1024, 131072);
        }

        /// <summary>
        /// The sampling temperature for generation.
        /// Clamped to the range 0.0-2.0.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double Temperature
        {
            get => _Temperature;
            set => _Temperature = Math.Clamp(value, 0.0, 2.0);
        }

        /// <summary>
        /// The total context window size in tokens.
        /// Clamped to the range 1024-1048576.
        /// </summary>
        [JsonPropertyName("contextWindow")]
        public int ContextWindow
        {
            get => _ContextWindow;
            set => _ContextWindow = Math.Clamp(value, 1024, 1048576);
        }

        /// <summary>
        /// The HTTP request timeout in milliseconds for bounded requests.
        /// Clamped to a minimum of 10000 (10 seconds). Defaults to 120000 (2 minutes).
        /// Streaming chat responses may exceed this value and are cancelled by the caller instead.
        /// </summary>
        [JsonPropertyName("timeoutMs")]
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = Math.Max(value, 10000);
        }

        /// <summary>
        /// Custom HTTP headers to include in every request to this endpoint.
        /// Common use: authentication headers like Authorization or x-api-key.
        /// </summary>
        [JsonPropertyName("headers")]
        public Dictionary<string, string> Headers
        {
            get => _Headers;
            set => _Headers = value ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Whether tool calls should be auto-approved when this endpoint is active.
        /// </summary>
        [JsonPropertyName("autoApproveTools")]
        public bool AutoApproveTools
        {
            get => _AutoApproveTools;
            set => _AutoApproveTools = value;
        }

        /// <summary>
        /// Optional endpoint-scoped maximum number of agent loop iterations before forcing a stop.
        /// When null, the global <see cref="MuxSettings.MaxAgentIterations"/> value is used.
        /// Clamped to the range 1-100 when set.
        /// </summary>
        [JsonPropertyName("maxAgentIterations")]
        public int? MaxAgentIterations
        {
            get => _MaxAgentIterations;
            set => _MaxAgentIterations = value.HasValue
                ? Math.Clamp(value.Value, 1, 100)
                : null;
        }

        /// <summary>
        /// Optional backend-specific behavioral overrides.
        /// </summary>
        [JsonPropertyName("quirks")]
        public BackendQuirks? Quirks
        {
            get => _Quirks;
            set => _Quirks = value;
        }

        /// <summary>
        /// Optional reasoning effort selection for this endpoint. When null (the default), no reasoning
        /// field is sent and requests are unchanged. A selected level drives provider-appropriate defaults.
        /// </summary>
        [JsonPropertyName("reasoningEffort")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ReasoningEffortConfig? ReasoningEffort
        {
            get => _ReasoningEffort;
            set => _ReasoningEffort = value;
        }

        /// <summary>
        /// Whether the model's reasoning ("thinking") is captured and displayed when this endpoint is
        /// active. Defaults to false. When false, no reasoning is surfaced and behavior is unchanged.
        /// </summary>
        [JsonPropertyName("showThinking")]
        public bool ShowThinking
        {
            get => _ShowThinking;
            set => _ShowThinking = value;
        }

        /// <summary>
        /// Optional API key for adapters that authenticate with a key passed to the client rather than a raw
        /// header: <c>anthropic</c> (<c>x-api-key</c>), <c>gemini</c> (URL key), and <c>azure-openai</c>
        /// (<c>api-key</c> header). Accepts a literal value or a <c>${VAR}</c> environment reference. The
        /// OpenAI-family adapters continue to authenticate via <see cref="Headers"/>. Ignored by adapters that
        /// resolve credentials from the environment (<c>vertex</c>, <c>bedrock</c>).
        /// </summary>
        [JsonPropertyName("apiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiKey
        {
            get => _ApiKey;
            set => _ApiKey = value;
        }

        /// <summary>
        /// Cloud region for adapters that require one: <c>vertex</c> (e.g. <c>us-central1</c>) and
        /// <c>bedrock</c> (e.g. <c>us-east-1</c>). Accepts a literal value or a <c>${VAR}</c> reference.
        /// </summary>
        [JsonPropertyName("region")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Region
        {
            get => _Region;
            set => _Region = value;
        }

        /// <summary>
        /// Google Cloud project id, required by the <c>vertex</c> adapter. Accepts a literal value or a
        /// <c>${VAR}</c> reference.
        /// </summary>
        [JsonPropertyName("project")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Project
        {
            get => _Project;
            set => _Project = value;
        }

        /// <summary>
        /// Optional Azure OpenAI <c>api-version</c> for the <c>azure-openai</c> adapter. When null, PolyPrompt's
        /// default API version is used.
        /// </summary>
        [JsonPropertyName("apiVersion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiVersion
        {
            get => _ApiVersion;
            set => _ApiVersion = value;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a deep copy of this endpoint, including nested settings (headers, quirks, and reasoning
        /// effort), via a JSON round trip so no field is missed as new ones are added. The copy is independent:
        /// mutating it does not affect the original.
        /// </summary>
        /// <returns>A deep copy of this endpoint.</returns>
        public EndpointConfig Clone()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(this);
            return System.Text.Json.JsonSerializer.Deserialize<EndpointConfig>(json) ?? new EndpointConfig();
        }

        #endregion
    }
}
