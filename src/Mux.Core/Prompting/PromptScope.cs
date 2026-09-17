namespace Mux.Core.Prompting
{
    /// <summary>
    /// Where a catalog prompt's override is stored, which determines how it is resolved and edited.
    /// </summary>
    public enum PromptScope
    {
        /// <summary>
        /// The override lives inside the active, switchable <see cref="Mux.Core.Models.PromptProfile"/> in
        /// <c>prompts.json</c>. Users keep more than one profile and swap the active one.
        /// </summary>
        Profile,

        /// <summary>
        /// The override lives in the global <c>operational</c> map of <c>prompts.json</c>, independent of which
        /// profile is active, so a single copy is shared across every profile.
        /// </summary>
        Global,

        /// <summary>
        /// The prompt is owned by another subsystem (for example <c>subagents.json</c>) and is listed
        /// read-through in the catalog with a deep link to its dedicated editor rather than overridden here.
        /// </summary>
        External
    }
}
