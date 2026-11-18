namespace Mux.Core.Cloud
{
    using System;

    /// <summary>
    /// How one tunnel connection attempt ended, used to decide whether and when to reconnect.
    /// </summary>
    public class CloudConnectionOutcome
    {
        /// <summary>Whether the cloud accepted the handshake on this attempt.</summary>
        public bool Welcomed { get; set; } = false;

        /// <summary>Whether the machine must stop reconnecting until the user acts.</summary>
        public bool Terminal { get; set; } = false;

        /// <summary>State to show when <see cref="Terminal"/> is true.</summary>
        public CloudStateEnum TerminalState { get; set; } = CloudStateEnum.Disabled;

        /// <summary>Human-readable reason.</summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>WebSocket close code, when the socket closed cleanly.</summary>
        public int? CloseCode { get; set; } = null;

        /// <summary>Delay the cloud asked for (<c>tunnel.goaway</c>), or null.</summary>
        public TimeSpan? RetryAfter { get; set; } = null;
    }
}
