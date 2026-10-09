namespace Mux.Core.Memory
{
    using System;

    /// <summary>
    /// Parses the <c>#</c> quick-add syntax shared by the terminal and desktop composers: <c># text</c> saves a project
    /// memory and <c>#global text</c> a global one, with no model call. Markdown headings (<c>##</c>) and a bare
    /// <c>#</c> are not quick-adds.
    /// </summary>
    public static class MemoryQuickAdd
    {
        #region Public-Methods

        /// <summary>
        /// Recognizes a quick-add prompt.
        /// </summary>
        /// <param name="prompt">The submitted text.</param>
        /// <param name="scope">The scope to save in.</param>
        /// <param name="text">The text to remember.</param>
        /// <returns>True when the prompt is a quick-add.</returns>
        public static bool TryParse(string? prompt, out MemoryScopeEnum scope, out string text)
        {
            scope = MemoryScopeEnum.Project;
            text = string.Empty;
            string trimmed = (prompt ?? string.Empty).Trim();
            if (!trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith("##", StringComparison.Ordinal))
            {
                return false;
            }

            string rest = trimmed.Substring(1).Trim();
            if (rest.Length > 7 && rest.StartsWith("global", StringComparison.OrdinalIgnoreCase) && char.IsWhiteSpace(rest[6]))
            {
                scope = MemoryScopeEnum.Global;
                rest = rest.Substring(7).Trim();
            }

            if (MemoryStore.NameFromText(rest).Length == 0)
            {
                return false;
            }

            text = rest;
            return true;
        }

        /// <summary>
        /// Saves a quick-add and returns the confirmation line to show.
        /// </summary>
        /// <param name="store">The store. Must not be null.</param>
        /// <param name="scope">The scope.</param>
        /// <param name="text">The text to remember.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The confirmation, for example <c>Remembered project memory 'tests-need-docker'.</c></returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public static string Save(MemoryStore store, MemoryScopeEnum scope, string text, string? workingDirectory)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));

            MemoryEntry entry = store.Save(MemoryStore.NameFromText(text), text, text, scope, workingDirectory, out bool created);
            return (created ? "Remembered " : "Updated ") + (scope == MemoryScopeEnum.Global ? "global" : "project") + " memory '" + entry.Slug + "'.";
        }

        #endregion
    }
}
