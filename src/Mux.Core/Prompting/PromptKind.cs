namespace Mux.Core.Prompting
{
    /// <summary>
    /// The category of a model-facing prompt in the <see cref="PromptCatalog"/>. The kind is a first-class
    /// property so a Prompts view can group and label the whole catalog uniformly, and so the "type of use"
    /// of a prompt is explicit rather than inferred from which field a string happens to live in. Each kind's
    /// stable wire name (used by the REST catalog and every surface) is produced by
    /// <see cref="PromptKindExtensions.ToWireString(PromptKind)"/>.
    /// </summary>
    public enum PromptKind
    {
        /// <summary>
        /// A persona prompt that lives inside a switchable <see cref="Mux.Core.Models.PromptProfile"/> —
        /// the main system prompt and its tools-disabled variant. Scope is per-profile.
        /// </summary>
        SystemPersona,

        /// <summary>
        /// A prompt used by the automatic history-compaction sidecar: its system prompt, the user framing that
        /// introduces the history to compact, and the prefix applied to a synthetic summary turn. Scope is global.
        /// </summary>
        Compaction,

        /// <summary>
        /// The task-planning guidance block appended to the system prompt when task planning is enabled. Scope is global.
        /// </summary>
        TaskPlanning,

        /// <summary>
        /// A prompt used to generate a short session title from the opening exchange. Scope is global.
        /// </summary>
        TitleGeneration,

        /// <summary>
        /// A lead-in for a dynamically composed prompt section (the skills section, the MCP section). Scope is global.
        /// </summary>
        ToolSection,

        /// <summary>
        /// The description string a tool advertises to the model. Editing one is a power-user affordance; a
        /// mangled description can degrade tool use, so entries carry required placeholders and a reset. Scope is global.
        /// </summary>
        ToolDescription,

        /// <summary>
        /// A structured status payload returned to the model in a tool result (denial, execution error, unknown
        /// tool, digest role labels). Structured strings whose editability is a power-user affordance. Scope is global.
        /// </summary>
        ToolResult,

        /// <summary>
        /// A prompt used by a diagnostic probe of an endpoint. Scope is global.
        /// </summary>
        Diagnostics,

        /// <summary>
        /// A prompt used to build a large-file context block — the structural map header and the map/reduce
        /// prompts of the iterative summarizer. Scope is global.
        /// </summary>
        FileContext,

        /// <summary>
        /// A subagent seed persona. These keep their own home in <c>subagents.json</c> and their own manager on
        /// every surface; the catalog lists them read-through with a deep link rather than duplicating them. Scope is external.
        /// </summary>
        SubagentPersona
    }
}
