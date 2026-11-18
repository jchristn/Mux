namespace Mux.Core.Cloud
{
    using System;

    /// <summary>
    /// A snapshot of the Mux Cloud connection, for display on every surface and for <c>GET /v1.0/api/cloud/status</c>.
    /// </summary>
    public class CloudStatus
    {
        #region Public-Members

        /// <summary>Connection state.</summary>
        public CloudStateEnum State { get; set; } = CloudStateEnum.Disabled;

        /// <summary>State name as shown to users (<c>Disabled</c>, <c>Connecting</c>, <c>Connected</c>, <c>Reconnecting</c>, <c>SignedOut</c>, <c>Revoked</c>).</summary>
        public string StateName => State.ToString();

        /// <summary>Why the connection is in this state (the last error or close reason), or empty.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>Mux Cloud server URL.</summary>
        public string ServerUrl { get; set; } = string.Empty;

        /// <summary>Enrolled machine identifier, or empty.</summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>Machine display name (the cloud's name once connected).</summary>
        public string MachineName { get; set; } = string.Empty;

        /// <summary>Account email the machine is enrolled to, or empty.</summary>
        public string AccountEmail { get; set; } = string.Empty;

        /// <summary>Whether remote users may approve tools on this machine.</summary>
        public bool AllowRemoteToolApproval { get; set; } = true;

        /// <summary>Whether this process is the one running the tunnel (false when another process holds it).</summary>
        public bool HostedHere { get; set; } = false;

        /// <summary>Current connection identifier, or empty.</summary>
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>When the current connection was accepted (UTC), or null.</summary>
        public DateTime? ConnectedUtc { get; set; } = null;

        /// <summary>Round trip of the last heartbeat in milliseconds, or null.</summary>
        public double? LatencyMs { get; set; } = null;

        /// <summary>Seconds until the next reconnect attempt while <see cref="CloudStateEnum.Reconnecting"/>, or null.</summary>
        public double? RetryInSeconds { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A one-line summary, for example <c>Connected to https://app.usemux.ai as studio-mac</c>.
        /// </summary>
        /// <returns>Summary text.</returns>
        public string Describe()
        {
            switch (State)
            {
                case CloudStateEnum.Connected:
                    return "Connected to " + ServerUrl + " as " + MachineName + (string.IsNullOrEmpty(AccountEmail) ? string.Empty : " (" + AccountEmail + ")");
                case CloudStateEnum.Connecting:
                    return "Connecting to " + ServerUrl;
                case CloudStateEnum.Reconnecting:
                    return "Reconnecting to " + ServerUrl + (string.IsNullOrEmpty(Reason) ? string.Empty : ": " + Reason);
                case CloudStateEnum.SignedOut:
                    return "Signed out of Mux Cloud" + (string.IsNullOrEmpty(Reason) ? string.Empty : ": " + Reason) + ". Sign in again.";
                case CloudStateEnum.Revoked:
                    return "This machine was removed from Mux Cloud. Sign in again to reconnect it.";
                default:
                    return string.IsNullOrEmpty(Reason) ? "Not connected to Mux Cloud" : "Not connected to Mux Cloud: " + Reason;
            }
        }

        /// <summary>Copy this snapshot.</summary>
        /// <returns>A copy.</returns>
        public CloudStatus Clone()
        {
            return (CloudStatus)MemberwiseClone();
        }

        #endregion
    }
}
