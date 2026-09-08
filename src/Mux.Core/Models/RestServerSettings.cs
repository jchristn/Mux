namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Configuration for the optional local REST server and system-tray agent. The server is opt-in: a plain
    /// interactive or headless run never starts it. It is started only by <c>mux serve</c> or the tray agent,
    /// and binds to loopback by default. See <c>docs/REST_API.md</c>.
    /// </summary>
    public class RestServerSettings
    {
        #region Private-Members

        private bool _Enabled = false;
        private string _Hostname = "127.0.0.1";
        private int _Port = 8710;
        private bool _Ssl = false;
        private string? _ApiKey = null;
        private string _CorsAllowOrigin = "*";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="RestServerSettings"/> class with default values.
        /// </summary>
        public RestServerSettings()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether the tray agent should start the REST server automatically. The <c>mux serve</c> command
        /// starts the server regardless of this flag. Defaults to false.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// The hostname the server binds to. Defaults to <c>127.0.0.1</c> (loopback). Pinning IPv4 loopback
        /// avoids the Windows <c>localhost</c>/IPv6 first-resolution latency.
        /// </summary>
        [JsonPropertyName("hostname")]
        public string Hostname
        {
            get => _Hostname;
            set => _Hostname = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value;
        }

        /// <summary>
        /// The TCP port the server binds to. Clamped to the range 1-65535. Defaults to 8710.
        /// </summary>
        [JsonPropertyName("port")]
        public int Port
        {
            get => _Port;
            set => _Port = Math.Clamp(value, 1, 65535);
        }

        /// <summary>
        /// Whether the server binds with SSL. Defaults to false (loopback plaintext).
        /// </summary>
        [JsonPropertyName("ssl")]
        public bool Ssl
        {
            get => _Ssl;
            set => _Ssl = value;
        }

        /// <summary>
        /// The API key required on requests (as <c>Authorization: Bearer &lt;key&gt;</c> or <c>X-Api-Key</c>).
        /// When null or blank, <c>mux serve</c> auto-generates one on first start and persists it. Supports a
        /// literal value or a <c>${VAR}</c> environment reference.
        /// </summary>
        [JsonPropertyName("apiKey")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ApiKey
        {
            get => _ApiKey;
            set => _ApiKey = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// The value sent in the <c>Access-Control-Allow-Origin</c> header. Defaults to <c>*</c> (development
        /// default); tighten to a known origin when exposing the server beyond trusted local callers.
        /// </summary>
        [JsonPropertyName("corsAllowOrigin")]
        public string CorsAllowOrigin
        {
            get => _CorsAllowOrigin;
            set => _CorsAllowOrigin = string.IsNullOrWhiteSpace(value) ? "*" : value;
        }

        #endregion
    }
}
