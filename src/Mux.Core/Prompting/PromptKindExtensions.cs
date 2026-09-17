namespace Mux.Core.Prompting
{
    using System;

    /// <summary>
    /// Maps <see cref="PromptKind"/> values to and from the stable kebab-case wire strings used by the REST
    /// catalog and every surface, so the enum can evolve without breaking the persisted or transmitted name.
    /// </summary>
    public static class PromptKindExtensions
    {
        /// <summary>
        /// Returns the stable kebab-case wire string for a kind (for example <c>tool-description</c>).
        /// </summary>
        /// <param name="kind">The kind to convert.</param>
        /// <returns>The wire string; never null.</returns>
        public static string ToWireString(this PromptKind kind)
        {
            switch (kind)
            {
                case PromptKind.SystemPersona: return "system-persona";
                case PromptKind.Compaction: return "compaction";
                case PromptKind.TaskPlanning: return "task-planning";
                case PromptKind.TitleGeneration: return "title-generation";
                case PromptKind.ToolSection: return "tool-section";
                case PromptKind.ToolDescription: return "tool-description";
                case PromptKind.ToolResult: return "tool-result";
                case PromptKind.Diagnostics: return "diagnostics";
                case PromptKind.FileContext: return "file-context";
                case PromptKind.SubagentPersona: return "subagent-persona";
                default: return kind.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Parses a kebab-case wire string back to a <see cref="PromptKind"/>. Matching is case-insensitive.
        /// </summary>
        /// <param name="value">The wire string to parse; null or empty is rejected.</param>
        /// <param name="kind">The parsed kind when the return value is true; otherwise the default.</param>
        /// <returns>True when <paramref name="value"/> maps to a known kind; otherwise false.</returns>
        public static bool TryParseWireString(string? value, out PromptKind kind)
        {
            kind = default;
            if (string.IsNullOrWhiteSpace(value)) return false;

            foreach (PromptKind candidate in (PromptKind[])Enum.GetValues(typeof(PromptKind)))
            {
                if (string.Equals(candidate.ToWireString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    kind = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
