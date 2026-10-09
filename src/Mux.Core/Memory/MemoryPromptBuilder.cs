namespace Mux.Core.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Builds the system-prompt section that lists the remembered facts for a project (and the global ones) as an
    /// index of names and descriptions, newest first, within a byte budget. Older entries that do not fit are left
    /// out with a note, so the model knows to use <c>recall</c>.
    /// </summary>
    public static class MemoryPromptBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Builds the memory section.
        /// </summary>
        /// <param name="store">The memory store. Must not be null.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <param name="maxBytes">The most UTF-8 bytes the index lines may use; 0 omits the index.</param>
        /// <returns>The section, starting with a blank line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public static string Build(MemoryStore store, string? workingDirectory, int maxBytes)
        {
            if (store is null) throw new ArgumentNullException(nameof(store));

            StringBuilder builder = new StringBuilder();
            builder.Append("\n\n## Memory\n\n");
            builder.Append("Facts the user asked you to keep across sessions. Use `recall` to read one in full or to search, ");
            builder.Append("`remember` to save a durable fact, preference, or decision the user wants kept (never secrets), ");
            builder.Append("and `forget` to remove one that is wrong or outdated. Memories may be stale; verify before relying on them.\n");

            List<MemoryEntry> project = store.List(MemoryScopeEnum.Project, workingDirectory);
            List<MemoryEntry> global = store.List(MemoryScopeEnum.Global, workingDirectory);
            if (project.Count + global.Count == 0)
            {
                builder.Append("\nNo memories are saved yet.\n");
                return builder.ToString();
            }

            int budget = Math.Max(0, maxBytes);
            int used = 0;
            int omitted = 0;
            bool full = false;
            AppendScope(builder, "Project memories", project, budget, ref used, ref omitted, ref full);
            AppendScope(builder, "Global memories", global, budget, ref used, ref omitted, ref full);
            if (omitted > 0)
            {
                builder.Append("\n(").Append(omitted).Append(omitted == 1 ? " older memory is" : " older memories are")
                    .Append(" not listed because of the memoryMaxBytes limit; use recall to search them.)\n");
            }

            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private static void AppendScope(StringBuilder builder, string heading, List<MemoryEntry> entries, int budget, ref int used, ref int omitted, ref bool full)
        {
            if (entries.Count == 0)
            {
                return;
            }

            StringBuilder lines = new StringBuilder();
            foreach (MemoryEntry entry in entries)
            {
                string line = "- " + entry.Slug + ": " + entry.Description + "\n";
                int bytes = Encoding.UTF8.GetByteCount(line);
                // Entries are newest first; once one does not fit, it and everything older are left out.
                if (full || used + bytes > budget)
                {
                    full = true;
                    omitted++;
                    continue;
                }

                used += bytes;
                lines.Append(line);
            }

            if (lines.Length > 0)
            {
                builder.Append('\n').Append(heading).Append(":\n").Append(lines);
            }
        }

        #endregion
    }
}
