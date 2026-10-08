namespace Mux.Core.Enums
{
    /// <summary>
    /// Where a loaded skill came from. A project skill lives in a directory inside the repository (for
    /// example <c>.mux/skills</c> or <c>.claude/skills</c>) and shadows a user skill with the same id.
    /// </summary>
    public enum SkillScopeEnum
    {
        /// <summary>
        /// A skill from the user's skills directory (<c>~/.mux/skills</c> by default), including the seeded
        /// defaults.
        /// </summary>
        User = 0,

        /// <summary>
        /// A skill checked into the current project, discovered under one of the configured project skill
        /// roots relative to the repository root.
        /// </summary>
        Project = 1
    }
}
