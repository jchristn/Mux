namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;
    using MuxCloud.Protocol;

    /// <summary>
    /// Mux Cloud connection settings (<c>settings.json</c> <c>cloud</c> block). When <see cref="Enabled"/> is true
    /// and the machine is enrolled, the process that owns the local hub port keeps one outbound WebSocket to Mux
    /// Cloud so the cloud dashboard and mobile app can use this machine as if they were at it. See
    /// <c>WEBSOCKETS_API.md</c> and <c>docs/CONFIG.md</c>.
    /// </summary>
    public class CloudSettings
    {
        #region Private-Members

        private bool _Enabled = false;
        private string _ServerUrl = TunnelProtocol.DefaultServerUrl;
        private string? _MachineId = null;
        private string? _MachineName = null;
        private string? _MachineSecret = null;
        private string? _AccountEmail = null;
        private bool _AllowRemoteToolApproval = true;
        private int _HeartbeatSeconds = 20;
        private int _ReconnectMaxDelaySeconds = 60;
        private bool _AllowInvalidCertificates = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CloudSettings"/> class with default values.
        /// </summary>
        public CloudSettings()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether this machine connects to Mux Cloud. Set by <c>mux cloud login</c>; cleared by
        /// <c>mux cloud logout</c>. Default false.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Mux Cloud server URL. Default <c>https://app.usemux.ai</c>. Blank resets to the default. Point it at
        /// your own server to self-host.
        /// </summary>
        [JsonPropertyName("serverUrl")]
        public string ServerUrl
        {
            get => _ServerUrl;
            set => _ServerUrl = string.IsNullOrWhiteSpace(value) ? TunnelProtocol.DefaultServerUrl : value.Trim().TrimEnd('/');
        }

        /// <summary>
        /// Machine identifier issued at enrollment (prefix <c>mch_</c>), or null when not enrolled.
        /// </summary>
        [JsonPropertyName("machineId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MachineId
        {
            get => _MachineId;
            set => _MachineId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Display name for this machine in Mux Cloud. Null uses the host name.
        /// </summary>
        [JsonPropertyName("machineName")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MachineName
        {
            get => _MachineName;
            set => _MachineName = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Machine secret issued at enrollment (prefix <c>mcs_</c>). It is this machine's Mux Cloud credential:
        /// anyone who can read it can connect as this machine. Masked in every API response. Null when not enrolled.
        /// </summary>
        [JsonPropertyName("machineSecret")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? MachineSecret
        {
            get => _MachineSecret;
            set => _MachineSecret = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Email of the Mux Cloud account this machine is enrolled to, for display only. Null when not enrolled.
        /// </summary>
        [JsonPropertyName("accountEmail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? AccountEmail
        {
            get => _AccountEmail;
            set => _AccountEmail = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Whether a user connected through Mux Cloud may approve tools that write files or run commands on this
        /// machine. Default true. When false, relayed runs deny tools that would prompt; local use is unaffected.
        /// </summary>
        [JsonPropertyName("allowRemoteToolApproval")]
        public bool AllowRemoteToolApproval
        {
            get => _AllowRemoteToolApproval;
            set => _AllowRemoteToolApproval = value;
        }

        /// <summary>
        /// Heartbeat interval this machine proposes, in seconds. The server's welcome value wins. Default 20,
        /// minimum 5, maximum 300.
        /// </summary>
        [JsonPropertyName("heartbeatSeconds")]
        public int HeartbeatSeconds
        {
            get => _HeartbeatSeconds;
            set => _HeartbeatSeconds = Math.Clamp(value, 5, 300);
        }

        /// <summary>
        /// Largest delay between reconnect attempts, in seconds. Default 60, minimum 5, maximum 3600.
        /// </summary>
        [JsonPropertyName("reconnectMaxDelaySeconds")]
        public int ReconnectMaxDelaySeconds
        {
            get => _ReconnectMaxDelaySeconds;
            set => _ReconnectMaxDelaySeconds = Math.Clamp(value, 5, 3600);
        }

        /// <summary>
        /// Accept invalid TLS certificates from the Mux Cloud server (self-hosted development only). Default false.
        /// </summary>
        [JsonPropertyName("allowInvalidCertificates")]
        public bool AllowInvalidCertificates
        {
            get => _AllowInvalidCertificates;
            set => _AllowInvalidCertificates = value;
        }

        /// <summary>
        /// Whether the machine holds a credential (<see cref="MachineId"/> and <see cref="MachineSecret"/>).
        /// </summary>
        [JsonIgnore]
        public bool IsEnrolled => !string.IsNullOrEmpty(_MachineId) && !string.IsNullOrEmpty(_MachineSecret);

        #endregion
    }
}
