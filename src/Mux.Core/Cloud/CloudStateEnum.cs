namespace Mux.Core.Cloud
{
    /// <summary>
    /// Mux Cloud connection state, shown with the same names on every surface.
    /// </summary>
    public enum CloudStateEnum
    {
        /// <summary>Not connected: the machine is not enrolled or the connection is turned off.</summary>
        Disabled = 0,

        /// <summary>Opening the tunnel for the first time.</summary>
        Connecting = 1,

        /// <summary>The tunnel is up and the cloud accepted the handshake.</summary>
        Connected = 2,

        /// <summary>The tunnel dropped and is retrying with backoff. <see cref="CloudStatus.Reason"/> says why.</summary>
        Reconnecting = 3,

        /// <summary>The cloud rejected the machine credential. Sign in again.</summary>
        SignedOut = 4,

        /// <summary>The machine was removed or revoked in Mux Cloud. Sign in again to re-enroll.</summary>
        Revoked = 5
    }
}
