namespace Mux.Core.Interaction
{
    /// <summary>
    /// The interactive surfaces' turn mode, cycled with Shift+Tab in the terminal: normal approvals, auto-approve
    /// everything, or plan mode (read-only exploration that ends with a plan for approval).
    /// </summary>
    public enum InteractionModeEnum
    {
        /// <summary>The session's configured approval policy.</summary>
        Normal = 0,

        /// <summary>Every tool call is approved automatically.</summary>
        AutoApprove = 1,

        /// <summary>Read-only planning; the turn ends with <c>exit_plan</c>.</summary>
        Plan = 2
    }
}
