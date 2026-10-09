namespace Mux.Core.Memory
{
    /// <summary>
    /// Where a memory applies: to the current project (repository) only, or to every project.
    /// </summary>
    public enum MemoryScopeEnum
    {
        /// <summary>
        /// The memory belongs to one project, keyed by its repository root (or working directory outside git).
        /// </summary>
        Project = 0,

        /// <summary>
        /// The memory applies everywhere (user-wide preferences and facts).
        /// </summary>
        Global = 1
    }
}
