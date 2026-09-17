namespace Mux.Desktop.Prompting
{
    using System.Collections.Generic;

    /// <summary>
    /// A single operational-prompt catalog entry projected for the desktop catalog table: its metadata, the
    /// coded default, the current effective value, and whether an override is in effect. This is a pure
    /// projection produced by <see cref="PromptCatalogViewModel"/> so the Avalonia view stays thin and the
    /// projection logic is unit-testable in <c>Mux.Desktop.Core</c>.
    /// </summary>
    public sealed class PromptCatalogRow
    {
        /// <summary>The stable catalog key (for example <c>tool.read_file</c>).</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>The kind wire string used to group the row (for example <c>tool-description</c>).</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Where an override is stored: <c>Profile</c>, <c>Global</c>, or <c>External</c>.</summary>
        public string Scope { get; set; } = string.Empty;

        /// <summary>The short human label.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The one-line explanation of the prompt's purpose.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>The placeholder tokens an override must preserve (for example <c>{ToolName}</c>).</summary>
        public IReadOnlyList<string> Placeholders { get; set; } = new List<string>();

        /// <summary>The coded default content.</summary>
        public string Default { get; set; } = string.Empty;

        /// <summary>The current effective content (the override when set, otherwise the default).</summary>
        public string Effective { get; set; } = string.Empty;

        /// <summary>Whether a non-empty override is currently in effect.</summary>
        public bool Overridden { get; set; }

        /// <summary>Whether this row can carry an operational override (true only for global-scoped entries).</summary>
        public bool Editable { get; set; }
    }
}
