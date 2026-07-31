namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// Assembles a complete, valid <c>SKILL.md</c> for a seeded default skill from its manifest fields and a
    /// list of commands, so the category files declare skills as data rather than hand-formatting frontmatter.
    /// </summary>
    public static class DefaultSkillBuilder
    {
        /// <summary>
        /// Builds the <c>SKILL.md</c> content for a default skill.
        /// </summary>
        /// <param name="id">The skill id (and folder name).</param>
        /// <param name="title">The human title.</param>
        /// <param name="description">The one- or two-sentence description.</param>
        /// <param name="mutating">Whether the skill's commands mutate the workspace.</param>
        /// <param name="tags">The tags, joined into an inline list.</param>
        /// <param name="whenToUse">The when-to-use guidance.</param>
        /// <param name="commands">The commands the skill declares. Must not be null or empty.</param>
        /// <returns>The complete <c>SKILL.md</c> content.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="commands"/> is empty.</exception>
        public static string Build(
            string id,
            string title,
            string description,
            bool mutating,
            string tags,
            string whenToUse,
            IReadOnlyList<DefaultSkillCommandDef> commands)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (title == null) throw new ArgumentNullException(nameof(title));
            if (description == null) throw new ArgumentNullException(nameof(description));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (commands.Count == 0) throw new ArgumentException("A default skill needs at least one command.", nameof(commands));

            StringBuilder builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("name: ").Append(id).Append('\n');
            builder.Append("title: ").Append(title).Append('\n');
            builder.Append("description: ").Append(description).Append('\n');
            builder.Append("version: 1.0.0\n");
            builder.Append("mutating: ").Append(mutating ? "true" : "false").Append('\n');
            builder.Append("whenToUse: ").Append(whenToUse).Append('\n');
            builder.Append("tags: [").Append(tags).Append("]\n");
            builder.Append("commands:\n");
            foreach (DefaultSkillCommandDef command in commands)
            {
                builder.Append("  - name: ").Append(command.Name).Append('\n');
                builder.Append("    description: ").Append(command.Description).Append('\n');
                builder.Append("    block: ").Append(command.Name).Append('\n');
                builder.Append("    interpreter: ").Append(command.Interpreter).Append('\n');
            }

            builder.Append("---\n\n");
            builder.Append("## ").Append(title).Append("\n\n");
            builder.Append(whenToUse).Append("\n\n");

            foreach (DefaultSkillCommandDef command in commands)
            {
                string fenceLanguage = command.Interpreter == "node" ? "js" : command.Interpreter;
                builder.Append("### ").Append(command.Name).Append(" — ").Append(command.Description).Append("\n\n");
                builder.Append("```").Append(fenceLanguage).Append(" id=").Append(command.Name).Append('\n');
                builder.Append(command.Code);
                if (!command.Code.EndsWith("\n", StringComparison.Ordinal))
                {
                    builder.Append('\n');
                }

                builder.Append("```\n\n");
            }

            return builder.ToString();
        }
    }
}
