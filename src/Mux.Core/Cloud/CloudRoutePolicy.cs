namespace Mux.Core.Cloud
{
    /// <summary>
    /// The one remote-specific control Mux Cloud adds: whether a relayed caller may approve tools. Everything else a
    /// relayed caller does is identical to a local caller (the parity rule). The host updates
    /// <see cref="CloudConnected"/> and <see cref="AllowRemoteToolApproval"/> as the tunnel and settings change.
    /// <para>Thread safety: reads and writes are atomic; safe to share.</para>
    /// </summary>
    public sealed class CloudRoutePolicy
    {
        #region Private-Members

        private volatile bool _BaseInteractive;
        private volatile bool _CloudConnected;
        private volatile bool _AllowRemoteToolApproval = true;

        #endregion

        #region Public-Members

        /// <summary>Whether the server was started with interactive approvals (<c>mux serve --allow-tools</c>).</summary>
        public bool BaseInteractive
        {
            get => _BaseInteractive;
            set => _BaseInteractive = value;
        }

        /// <summary>Whether this machine's Mux Cloud tunnel is connected.</summary>
        public bool CloudConnected
        {
            get => _CloudConnected;
            set => _CloudConnected = value;
        }

        /// <summary>Mirror of <c>cloud.allowRemoteToolApproval</c>. Default true.</summary>
        public bool AllowRemoteToolApproval
        {
            get => _AllowRemoteToolApproval;
            set => _AllowRemoteToolApproval = value;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="baseInteractive">Whether interactive approvals are on regardless of Mux Cloud.</param>
        public CloudRoutePolicy(bool baseInteractive = false)
        {
            _BaseInteractive = baseInteractive;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a chat run should prompt for approval of tools that would otherwise be denied.
        /// Local callers: when interactive approvals are on, or when Mux Cloud is connected with remote approval
        /// allowed (so a hub that accepts remote approvals also accepts local ones). Relayed callers: only when
        /// remote approval is allowed and approvals are available at all.
        /// </summary>
        /// <param name="relayed">Whether the request came through the Mux Cloud relay.</param>
        /// <returns>True to prompt; false to deny prompting tools.</returns>
        public bool AllowsInteractiveApproval(bool relayed)
        {
            bool remoteOn = _CloudConnected && _AllowRemoteToolApproval;
            if (relayed)
            {
                return _AllowRemoteToolApproval && (_BaseInteractive || _CloudConnected);
            }

            return _BaseInteractive || remoteOn;
        }

        /// <summary>
        /// Whether a caller may answer a pending approval (REST <c>/chat/approve</c> or a WebSocket <c>approve</c> frame).
        /// </summary>
        /// <param name="relayed">Whether the request came through the Mux Cloud relay.</param>
        /// <returns>True when allowed.</returns>
        public bool AllowsApprovalDecision(bool relayed)
        {
            return !relayed || _AllowRemoteToolApproval;
        }

        #endregion
    }
}
