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
        /// <param name="appliesTo">Optional file globs that gate when the skill is listed; null or empty lists it everywhere.</param>
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
            IReadOnlyList<DefaultSkillCommandDef> commands,
            IReadOnlyList<string>? appliesTo = null)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (title == null) throw new ArgumentNullException(nameof(title));
            if (description == null) throw new ArgumentNullException(nameof(description));
            if (commands == null) throw new ArgumentNullException(nameof(commands));
            if (commands.Count == 0) throw new ArgumentException("A default skill needs at least one command.", nameof(commands));

            List<string> tagList = new List<string>();
            foreach (string tag in (tags ?? string.Empty).Split(','))
            {
                string trimmed = tag.Trim();
                if (trimmed.Length > 0)
                {
                    tagList.Add(trimmed);
                }
            }

            return Build(new DefaultSkillDef
            {
                Id = id,
                Title = title,
                Description = description,
                Mutating = mutating,
                Tags = tagList,
                WhenToUse = whenToUse ?? string.Empty,
                AppliesTo = appliesTo == null ? new List<string>() : new List<string>(appliesTo),
                Commands = new List<DefaultSkillCommandDef>(commands)
            });
        }

        /// <summary>
        /// Builds the <c>SKILL.md</c> content for a default skill from its definition. A definition with no
        /// commands produces a playbook skill, which must carry a non-empty <see cref="DefaultSkillDef.Body"/>.
        /// </summary>
        /// <param name="definition">The skill definition. Must not be null.</param>
        /// <returns>The complete <c>SKILL.md</c> content.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the id, title, or description is empty, or when the
        /// definition has neither commands nor a body.</exception>
        public static string Build(DefaultSkillDef definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Id)) throw new ArgumentException("A default skill needs an id.", nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Title)) throw new ArgumentException("Default skill '" + definition.Id + "' needs a title.", nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Description)) throw new ArgumentException("Default skill '" + definition.Id + "' needs a description.", nameof(definition));
            if (definition.Commands.Count == 0 && string.IsNullOrWhiteSpace(definition.Body))
            {
                throw new ArgumentException("Default skill '" + definition.Id + "' needs at least one command or a playbook body.", nameof(definition));
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("name: ").Append(definition.Id).Append('\n');
            builder.Append("title: ").Append(definition.Title).Append('\n');
            builder.Append("description: ").Append(definition.Description).Append('\n');
            builder.Append("version: 1.0.0\n");
            builder.Append("mutating: ").Append(definition.Mutating ? "true" : "false").Append('\n');
            builder.Append("whenToUse: ").Append(definition.WhenToUse).Append('\n');
            if (!string.IsNullOrWhiteSpace(definition.ArgumentHint))
            {
                builder.Append("argumentHint: \"").Append(definition.ArgumentHint.Replace("\"", "'")).Append("\"\n");
            }

            builder.Append("tags: [").Append(string.Join(", ", definition.Tags)).Append("]\n");
            if (definition.AppliesTo.Count > 0)
            {
                builder.Append("appliesTo: [").Append(string.Join(", ", definition.AppliesTo)).Append("]\n");
            }

            if (definition.RequiresTools.Count > 0)
            {
                builder.Append("requiresTools: [").Append(string.Join(", ", definition.RequiresTools)).Append("]\n");
            }

            if (definition.Commands.Count > 0)
            {
                builder.Append("commands:\n");
                foreach (DefaultSkillCommandDef command in definition.Commands)
                {
                    builder.Append("  - name: ").Append(command.Name).Append('\n');
                    builder.Append("    description: ").Append(command.Description).Append('\n');
                    builder.Append("    block: ").Append(command.Name).Append('\n');
                    builder.Append("    interpreter: ").Append(command.Interpreter).Append('\n');
                    if (command.TimeoutMs > 0)
                    {
                        builder.Append("    timeoutMs: ").Append(command.TimeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }

            builder.Append("---\n\n");
            builder.Append("## ").Append(definition.Title).Append("\n\n");
            builder.Append(definition.WhenToUse).Append("\n\n");

            if (!string.IsNullOrWhiteSpace(definition.Body))
            {
                builder.Append(definition.Body.Replace("\r\n", "\n").Trim()).Append("\n\n");
            }

            foreach (DefaultSkillCommandDef command in definition.Commands)
            {
                string fenceLanguage = command.Interpreter == "node" ? "js" : command.Interpreter;
                builder.Append("### ").Append(command.Name).Append(": ").Append(command.Description).Append("\n\n");
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
