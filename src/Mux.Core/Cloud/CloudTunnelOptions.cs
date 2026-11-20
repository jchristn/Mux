namespace Mux.Core.Cloud
{
    using System;
    using MuxCloud.Protocol;

    /// <summary>
    /// Settings for one <see cref="CloudTunnelClient"/>.
    /// </summary>
    public class CloudTunnelOptions
    {
        #region Private-Members

        private int _HeartbeatSeconds = 20;
        private int _ReconnectBaseDelaySeconds = 2;
        private int _ReconnectMaxDelaySeconds = 60;
        private int _HandshakeTimeoutSeconds = 15;
        private int _MissedPongLimit = 3;
        private int _MaxConcurrentStreams = 32;

        #endregion

        #region Public-Members

        /// <summary>Mux Cloud server URL. Default <see cref="TunnelProtocol.DefaultServerUrl"/>.</summary>
        public string ServerUrl { get; set; } = TunnelProtocol.DefaultServerUrl;

        /// <summary>Enrolled machine identifier.</summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>Machine secret.</summary>
        public string MachineSecret { get; set; } = string.Empty;

        /// <summary>Display name sent in the handshake.</summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>Account email, for status display.</summary>
        public string AccountEmail { get; set; } = string.Empty;

        /// <summary>Installed mux version sent in the handshake.</summary>
        public string MuxVersion { get; set; } = string.Empty;

        /// <summary>Whether remote tool approval is allowed, for status display.</summary>
        public bool AllowRemoteToolApproval { get; set; } = true;

        /// <summary>Accept invalid TLS certificates (self-hosted development).</summary>
        public bool AllowInvalidCertificates { get; set; } = false;

        /// <summary>Heartbeat interval used until the welcome sets one. Default 20, minimum 5, maximum 300.</summary>
        public int HeartbeatSeconds
        {
            get => _HeartbeatSeconds;
            set => _HeartbeatSeconds = Math.Clamp(value, 5, 300);
        }

        /// <summary>
        /// Fixed heartbeat interval that overrides the welcome value, for tests and diagnostics. Null (the default)
        /// uses the welcome's <c>heartbeatSeconds</c>.
        /// </summary>
        public TimeSpan? HeartbeatIntervalOverride { get; set; } = null;

        /// <summary>Unanswered pings that mean the tunnel is dead. Default 3, minimum 1, maximum 10.</summary>
        public int MissedPongLimit
        {
            get => _MissedPongLimit;
            set => _MissedPongLimit = Math.Clamp(value, 1, 10);
        }

        /// <summary>First reconnect delay, in seconds. Default 2, minimum 1, maximum 300.</summary>
        public int ReconnectBaseDelaySeconds
        {
            get => _ReconnectBaseDelaySeconds;
            set => _ReconnectBaseDelaySeconds = Math.Clamp(value, 1, 300);
        }

        /// <summary>Largest reconnect delay, in seconds. Default 60, minimum 1, maximum 3600.</summary>
        public int ReconnectMaxDelaySeconds
        {
            get => _ReconnectMaxDelaySeconds;
            set => _ReconnectMaxDelaySeconds = Math.Clamp(value, 1, 3600);
        }

        /// <summary>Seconds to wait for the welcome after sending the hello. Default 15, minimum 1, maximum 120.</summary>
        public int HandshakeTimeoutSeconds
        {
            get => _HandshakeTimeoutSeconds;
            set => _HandshakeTimeoutSeconds = Math.Clamp(value, 1, 120);
        }

        /// <summary>
        /// Concurrent stream and channel cap used until the welcome sets one. Default 32, minimum 1, maximum 1024.
        /// </summary>
        public int MaxConcurrentStreams
        {
            get => _MaxConcurrentStreams;
            set => _MaxConcurrentStreams = Math.Clamp(value, 1, 1024);
        }

        #endregion
    }
}
