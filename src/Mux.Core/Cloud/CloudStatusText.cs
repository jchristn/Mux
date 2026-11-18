namespace Mux.Core.Cloud
{
    using System;

    /// <summary>
    /// English labels and sentences for the Mux Cloud state, used by the CLI, the TUI, and the tray menu. Translated
    /// surfaces (the local dashboard, the desktop app, and VS Code) carry the same text in their own string tables,
    /// keyed by <see cref="CloudStateKeys"/> and <see cref="CloudReasonCodes"/>.
    /// <para>Thread safety: stateless; safe to call concurrently.</para>
    /// </summary>
    public static class CloudStatusText
    {
        #region Public-Methods

        /// <summary>
        /// The short label for a state key, for example <c>Not signed in</c>.
        /// </summary>
        /// <param name="stateKey">A <see cref="CloudStateKeys"/> value.</param>
        /// <returns>Label, or the key itself when unknown.</returns>
        public static string Label(string? stateKey)
        {
            switch (stateKey)
            {
                case CloudStateKeys.NotSignedIn: return "Not signed in";
                case CloudStateKeys.Off: return "Off";
                case CloudStateKeys.Connecting: return "Connecting";
                case CloudStateKeys.Connected: return "Connected";
                case CloudStateKeys.Reconnecting: return "Reconnecting";
                case CloudStateKeys.SignedOut: return "Signed out";
                case CloudStateKeys.Removed: return "Removed";
                default: return stateKey ?? string.Empty;
            }
        }

        /// <summary>
        /// The sentence for a reason code, or null when the code is unknown (show <see cref="CloudStatus.Reason"/>).
        /// </summary>
        /// <param name="reasonCode">A <see cref="CloudReasonCodes"/> value.</param>
        /// <returns>Sentence, or null.</returns>
        public static string? Message(string? reasonCode)
        {
            switch (reasonCode)
            {
                case CloudReasonCodes.NotSignedIn: return "This machine is not signed in to Mux Cloud.";
                case CloudReasonCodes.TurnedOff: return "The Mux Cloud connection is turned off on this machine.";
                case CloudReasonCodes.AgentNotRunning: return "The mux agent is not running. The connection starts when it runs.";
                case CloudReasonCodes.AnotherProcess: return "Another mux process on this machine runs the Mux Cloud connection.";
                case CloudReasonCodes.NotHostedHere: return "Another mux process on this machine runs the Mux Cloud connection.";
                case CloudReasonCodes.ClockSkew: return "This machine's clock is off by more than 2 minutes. Fix the clock and mux will reconnect.";
                case CloudReasonCodes.CredentialRejected: return "Mux Cloud no longer accepts this machine's sign-in. Sign in again.";
                case CloudReasonCodes.Removed: return "This machine was removed from Mux Cloud. Sign in again to reconnect it.";
                case CloudReasonCodes.UpgradeRequired: return "Mux Cloud needs a newer version of mux. Upgrade mux, then reconnect.";
                case CloudReasonCodes.Unreachable: return "Mux Cloud cannot be reached. mux keeps trying.";
                case CloudReasonCodes.ConnectionDropped: return "The connection to Mux Cloud dropped. mux keeps trying.";
                case CloudReasonCodes.HeartbeatTimeout: return "The connection to Mux Cloud stopped responding. mux keeps trying.";
                case CloudReasonCodes.Replaced: return "Another connection for this machine replaced this one.";
                case CloudReasonCodes.ServerRestarting: return "Mux Cloud is restarting. mux reconnects when it is back.";
                case CloudReasonCodes.RateLimited: return "Too many failed attempts. mux waits before trying again.";
                case CloudReasonCodes.HandshakeRejected: return "Mux Cloud did not accept the connection. mux keeps trying.";
                default: return null;
            }
        }

        /// <summary>
        /// The reason line to show under the state label: the sentence for the reason code, else the raw reason. Empty
        /// when the label already says it (a machine that is not signed in).
        /// </summary>
        /// <param name="status">Status snapshot.</param>
        /// <returns>Reason line, possibly empty.</returns>
        public static string ReasonLine(CloudStatus status)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            if (IsRedundant(status)) return string.Empty;
            return Message(status.ReasonCode) ?? status.Reason ?? string.Empty;
        }

        /// <summary>
        /// Whether the reason only repeats the state label, so surfaces should not show it.
        /// </summary>
        /// <param name="status">Status snapshot.</param>
        /// <returns>True when the reason adds nothing to the label.</returns>
        public static bool IsRedundant(CloudStatus status)
        {
            if (status == null) throw new ArgumentNullException(nameof(status));
            return status.StateKey == CloudStateKeys.NotSignedIn && (string.IsNullOrEmpty(status.ReasonCode) || status.ReasonCode == CloudReasonCodes.NotSignedIn);
        }

        #endregion
    }
}
