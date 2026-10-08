namespace Mux.Cli.App
{
    /// <summary>
    /// The kind of row shown in the skills manager list, kept in a list parallel to the option strings so a
    /// blank separator or a trailing action never has to be inferred from index math.
    /// </summary>
    internal enum SkillMenuAction
    {
        /// <summary>
        /// A skill row; selecting it opens the per-skill action menu.
        /// </summary>
        Manage,

        /// <summary>
        /// A non-actionable separator row.
        /// </summary>
        None,

        /// <summary>
        /// The create-a-new-skill action.
        /// </summary>
        New,

        /// <summary>
        /// The import-a-skill action.
        /// </summary>
        Import,

        /// <summary>
        /// The re-scan-the-library action.
        /// </summary>
        Reload,

        /// <summary>
        /// Show the details of a project skill (read-only; project skills are edited in the repository).
        /// </summary>
        ProjectInfo,

        /// <summary>
        /// Choose a trust level for the current project's checked-in skills.
        /// </summary>
        Trust
    }
}
