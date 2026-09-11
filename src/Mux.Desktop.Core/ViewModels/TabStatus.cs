namespace Mux.Desktop.ViewModels
{
    /// <summary>
    /// The attention/activity status of a workspace tab, shown as an indicator on the tab. Values are ordered
    /// by display priority: a tab reflects the highest-priority condition currently true.
    /// </summary>
    public enum TabStatus
    {
        /// <summary>Nothing pending; no in-flight turn.</summary>
        Idle,

        /// <summary>A turn is in flight.</summary>
        Running,

        /// <summary>New events or text arrived while the tab was not focused.</summary>
        Unread,

        /// <summary>The last turn ended in an error (until the tab is next focused).</summary>
        Error,

        /// <summary>A background tab is blocked awaiting tool approval — the highest-priority signal.</summary>
        NeedsApproval
    }
}
