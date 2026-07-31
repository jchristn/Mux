namespace Mux.Core.Skills
{
    using System;
    using System.Text;
    using Mux.Core.Models;

    /// <summary>
    /// Generates a starter <c>SKILL.md</c> from a <see cref="SkillScaffold"/>. The output is a valid,
    /// immediately-loadable skill with one runnable inline block, so a newly created skill works before the
    /// author edits anything.
    /// </summary>
    public static class SkillScaffoldWriter
    {
        /// <summary>
        /// Builds the <c>SKILL.md</c> text for the supplied scaffold.
        /// </summary>
        /// <param name="scaffold">The collected inputs. Must not be null.</param>
        /// <returns>The complete <c>SKILL.md</c> content.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="scaffold"/> is null.</exception>
        public static string Build(SkillScaffold scaffold)
        {
            if (scaffold == null) throw new ArgumentNullException(nameof(scaffold));

            string description = string.IsNullOrWhiteSpace(scaffold.Description)
                ? "Describe what this skill does."
                : scaffold.Description;

            StringBuilder builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("name: ").Append(scaffold.Id).Append('\n');
            builder.Append("title: ").Append(scaffold.Title).Append('\n');
            builder.Append("description: ").Append(description).Append('\n');
            builder.Append("version: 0.1.0\n");
            builder.Append("mutating: ").Append(scaffold.Mutating ? "true" : "false").Append('\n');
            builder.Append("whenToUse: Explain when the model should reach for this skill.\n");
            builder.Append("tags: []\n");
            builder.Append("commands:\n");
            builder.Append("  - name: run\n");
            builder.Append("    description: The starter command; edit it to do real work.\n");
            builder.Append("    block: run\n");
            builder.Append("    interpreter: ").Append(scaffold.Interpreter).Append('\n');
            builder.Append("---\n\n");
            builder.Append("## What this does\n\n");
            builder.Append(description).Append("\n\n");
            builder.Append("## How to use\n\n");
            builder.Append("Call `skill` with this skill's name to read these instructions, then `run_skill` ");
            builder.Append("with command `run`.\n\n");
            builder.Append(BuildStarterBlock(scaffold.Interpreter));

            return builder.ToString();
        }

        private static string BuildStarterBlock(string interpreter)
        {
            switch (interpreter.ToLowerInvariant())
            {
                case "pwsh":
                    return "```pwsh id=run\nWrite-Output 'Hello from the new skill. Edit scripts or this block to do real work.'\n```\n";
                case "python":
                    return "```python id=run\nprint('Hello from the new skill. Edit this block to do real work.')\n```\n";
                case "node":
                    return "```js id=run\nconsole.log('Hello from the new skill. Edit this block to do real work.');\n```\n";
                default:
                    return "```" + interpreter + " id=run\necho 'Hello from the new skill. Edit this block to do real work.'\n```\n";
            }
        }
    }
}
