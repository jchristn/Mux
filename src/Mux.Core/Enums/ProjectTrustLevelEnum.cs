namespace Mux.Core.Enums
{
    /// <summary>
    /// How much of a project's checked-in skills the user has chosen to trust. Playbook skills execute
    /// nothing, so they load at every level except <see cref="Ignore"/>; skills with runnable commands load
    /// only at <see cref="All"/>.
    /// </summary>
    public enum ProjectTrustLevelEnum
    {
        /// <summary>
        /// No decision has been recorded. Behaves like <see cref="PlaybooksOnly"/> until the user decides.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Every project skill loads, including skills whose commands run code from the repository.
        /// </summary>
        All = 1,

        /// <summary>
        /// Only playbook skills load; skills with commands are listed in the inventory as blocked.
        /// </summary>
        PlaybooksOnly = 2,

        /// <summary>
        /// No project skills load at all.
        /// </summary>
        Ignore = 3
    }
}
