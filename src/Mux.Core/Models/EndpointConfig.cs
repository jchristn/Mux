namespace Mux.Core.Models
{
    using System;
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
        private string? _ApiKey = null;
        private string? _BearerToken = null;
        private BackendQuirks? _Quirks = null;

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
        /// The HTTP request timeout in milliseconds. Clamped to a minimum of 10000 (10 seconds).
        /// Defaults to 120000 (2 minutes).
        /// </summary>
        [JsonPropertyName("timeoutMs")]
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = Math.Max(value, 10000);
        }

        /// <summary>
        /// The optional API key for authentication with this endpoint.
        /// </summary>
        [JsonPropertyName("apiKey")]
        public string? ApiKey
        {
            get => _ApiKey;
            set => _ApiKey = value;
        }

        /// <summary>
        /// An optional bearer token for endpoint authentication. When set, sent as the
        /// Authorization: Bearer header. Takes precedence over <see cref="ApiKey"/>.
        /// </summary>
        [JsonPropertyName("bearerToken")]
        public string? BearerToken
        {
            get => _BearerToken;
            set => _BearerToken = value;
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

        #endregion
    }
}
