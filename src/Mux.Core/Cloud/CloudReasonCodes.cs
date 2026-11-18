namespace Mux.Core.Cloud
{
    /// <summary>
    /// Stable codes for why the Mux Cloud connection is in its current state. Surfaces map a known code to their own
    /// translated sentence and fall back to <see cref="CloudStatus.Reason"/> (English, with detail) for an empty or
    /// unknown code. The values are part of <c>GET /v1.0/api/cloud/status</c> and never change.
    /// <para>Thread safety: constants only.</para>
    /// </summary>
    public static class CloudReasonCodes
    {
        #region Public-Members

        /// <summary>The machine has never signed in to Mux Cloud, or it signed out.</summary>
        public const string NotSignedIn = "not_signed_in";

        /// <summary>The machine is signed in but the connection is turned off in settings.</summary>
        public const string TurnedOff = "turned_off";

        /// <summary>The machine is signed in but no mux process is running the tunnel (start the mux agent).</summary>
        public const string AgentNotRunning = "agent_not_running";

        /// <summary>Another mux process on this machine runs the tunnel.</summary>
        public const string AnotherProcess = "another_process";

        /// <summary>This mux process does not run the tunnel (another process on the machine owns it).</summary>
        public const string NotHostedHere = "not_hosted_here";

        /// <summary>The machine's clock is too far from Mux Cloud's for the handshake. mux keeps retrying.</summary>
        public const string ClockSkew = "clock_skew";

        /// <summary>Mux Cloud rejected the machine credential (<c>4401</c>). Sign in again.</summary>
        public const string CredentialRejected = "credential_rejected";

        /// <summary>The machine was removed or revoked in Mux Cloud (<c>4403</c>). Sign in again.</summary>
        public const string Removed = "removed";

        /// <summary>Mux Cloud needs a newer mux (<c>4426</c>).</summary>
        public const string UpgradeRequired = "upgrade_required";

        /// <summary>Mux Cloud could not be reached. mux keeps retrying.</summary>
        public const string Unreachable = "unreachable";

        /// <summary>The connection dropped. mux keeps retrying.</summary>
        public const string ConnectionDropped = "connection_dropped";

        /// <summary>Heartbeats stopped in one direction (<c>4408</c> or no pongs). mux keeps retrying.</summary>
        public const string HeartbeatTimeout = "heartbeat_timeout";

        /// <summary>A newer connection for this machine replaced this one (<c>4409</c>).</summary>
        public const string Replaced = "replaced";

        /// <summary>Mux Cloud is restarting (<c>4503</c>). mux reconnects when it is back.</summary>
        public const string ServerRestarting = "server_restarting";

        /// <summary>Too many failed attempts (<c>4429</c>). mux waits before retrying.</summary>
        public const string RateLimited = "rate_limited";

        /// <summary>Mux Cloud refused the handshake for another reason (<c>4400</c> or no reply).</summary>
        public const string HandshakeRejected = "handshake_rejected";

        #endregion
    }
}
