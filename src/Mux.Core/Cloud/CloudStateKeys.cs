namespace Mux.Core.Cloud
{
    /// <summary>
    /// What the user sees as the Mux Cloud state. It is <see cref="CloudStateEnum"/> with the not-connected state split
    /// in two: a machine that never signed in shows "Not signed in", and a signed-in machine whose connection is not
    /// running shows "Off". Surfaces translate these keys; <see cref="CloudStatus.StateName"/> keeps the enum names for
    /// API compatibility.
    /// <para>Thread safety: constants only.</para>
    /// </summary>
    public static class CloudStateKeys
    {
        #region Public-Members

        /// <summary>The machine is not signed in to Mux Cloud.</summary>
        public const string NotSignedIn = "not_signed_in";

        /// <summary>The machine is signed in, but the connection is off or nothing is running it.</summary>
        public const string Off = "off";

        /// <summary>Opening the tunnel for the first time.</summary>
        public const string Connecting = "connecting";

        /// <summary>The tunnel is up.</summary>
        public const string Connected = "connected";

        /// <summary>The tunnel dropped and mux is retrying.</summary>
        public const string Reconnecting = "reconnecting";

        /// <summary>Mux Cloud rejected the credential. Sign in again.</summary>
        public const string SignedOut = "signed_out";

        /// <summary>The machine was removed in Mux Cloud. Sign in again.</summary>
        public const string Removed = "removed";

        #endregion
    }
}
