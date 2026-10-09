namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using Mux.Core.Models;

    /// <summary>
    /// Turns a typed <c>/&lt;skill&gt; args</c> invocation into the user message that runs the skill. The
    /// skill's body becomes the message, with <c>$ARGUMENTS</c> replaced by the full argument text and
    /// <c>$1</c> through <c>$9</c> by positional arguments (split on whitespace, honoring single and double
    /// quotes). Substitution applies to prose only; fenced code blocks are left verbatim. When the body has
    /// no placeholder and arguments were given, they are appended on their own
    /// line. A skill with commands also gets a closing line telling the model to run them through
    /// <c>run_skill</c>. Every surface (terminal, print, desktop, web) uses this class so an invocation means
    /// the same thing everywhere. Stateless and thread-safe.
    /// </summary>
    public static class SkillInvocationExpander
    {
        #region Private-Members

        private static readonly Regex _PositionalPattern = new Regex("\\$([1-9])", RegexOptions.Compiled);
        private static readonly Regex _NamePattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Splits slash input into a candidate skill name and its argument text. Returns false when the input
        /// does not start with <c>/</c> or the name is not a well-formed skill id.
        /// </summary>
        /// <param name="input">The typed text. May be null.</param>
        /// <param name="name">The lowercased candidate skill name when the method returns true.</param>
        /// <param name="arguments">The trimmed argument text (possibly empty) when the method returns true.</param>
        /// <returns><c>true</c> when the input names a candidate skill.</returns>
        public static bool TryParse(string? input, out string name, out string arguments)
        {
            name = string.Empty;
            arguments = string.Empty;

            string trimmed = (input ?? string.Empty).Trim();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal) || trimmed.Length < 2)
            {
                return false;
            }

            trimmed = trimmed.Substring(1);
            int space = trimmed.IndexOfAny(new[] { ' ', '\t', '\n', '\r' });
            string token = space < 0 ? trimmed : trimmed.Substring(0, space);
            string rest = space < 0 ? string.Empty : trimmed.Substring(space + 1).Trim();

            string lowered = token.ToLowerInvariant();
            if (!_NamePattern.IsMatch(lowered))
            {
                return false;
            }

            name = lowered;
            arguments = rest;
            return true;
        }

        /// <summary>
        /// Expands an invocation of a skill into the message to submit.
        /// </summary>
        /// <param name="skill">The invoked skill. Must not be null.</param>
        /// <param name="arguments">The argument text. Null is treated as empty.</param>
        /// <returns>The expansion.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skill"/> is null.</exception>
        public static SkillInvocation Expand(Skill skill, string? arguments)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));

            string args = (arguments ?? string.Empty).Trim();
            List<string> positional = SplitArguments(args);
            string body = SkillPathResolver.Substitute(skill.Body, skill.DirectoryPath).Replace("\r\n", "\n").Trim();

            // Placeholders are substituted in prose only. Fenced code blocks are left verbatim, because a
            // shell block's own "$1" or "$ARGUMENTS" belongs to the script, not to the invocation.
            bool hadPlaceholder = false;
            StringBuilder substitutedBuilder = new StringBuilder();
            bool inFence = false;
            string[] lines = body.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                }
                else if (!inFence)
                {
                    if (line.Contains("$ARGUMENTS", StringComparison.Ordinal) || _PositionalPattern.IsMatch(line))
                    {
                        hadPlaceholder = true;
                    }

                    line = line.Replace("$ARGUMENTS", args, StringComparison.Ordinal);
                    line = _PositionalPattern.Replace(line, (Match match) =>
                    {
                        int index = match.Groups[1].Value[0] - '1';
                        return index < positional.Count ? positional[index] : string.Empty;
                    });
                }

                substitutedBuilder.Append(line);
                if (i < lines.Length - 1)
                {
                    substitutedBuilder.Append('\n');
                }
            }

            string substituted = substitutedBuilder.ToString();

            string name = skill.Manifest.Name;
            StringBuilder builder = new StringBuilder();
            builder.Append("Run the \"").Append(name).Append("\" skill");
            if (args.Length > 0)
            {
                builder.Append(" with these arguments: ").Append(args);
            }

            builder.Append(". Its instructions follow.\n\n");
            if (substituted.Length > 0)
            {
                builder.Append(substituted);
            }
            else
            {
                builder.Append(skill.Manifest.Description);
            }

            if (args.Length > 0 && !hadPlaceholder)
            {
                builder.Append("\n\nArguments: ").Append(args);
            }

            // Claude-format skills bundle scripts and references next to SKILL.md and expect to be run from that
            // folder. Say where it is (mux's own helper under resources/ needs no mention).
            List<string> bundled = SkillPathResolver.ListFiles(skill.DirectoryPath, out _);
            if (bundled.Exists((string file) => !file.StartsWith("resources/", StringComparison.Ordinal)))
            {
                builder.Append("\n\nThis skill's files are in ")
                    .Append(SkillPathResolver.NormalizeFolder(skill.DirectoryPath))
                    .Append("; paths such as scripts/ or references/ in its instructions are relative to that folder.");
            }

            if (skill.Manifest.Commands.Count > 0)
            {
                builder.Append("\n\nThis skill's commands run through the `run_skill` tool with name \"")
                    .Append(name)
                    .Append("\". Pick the command that matches the request and pass any arguments in `args`.");
            }

            return new SkillInvocation
            {
                SkillName = name,
                Arguments = args,
                Prompt = builder.ToString(),
                IsPlaybook = skill.Manifest.IsPlaybook
            };
        }

        /// <summary>
        /// Splits argument text on whitespace, keeping single- or double-quoted runs together with their
        /// quotes removed.
        /// </summary>
        /// <param name="arguments">The argument text. Null is treated as empty.</param>
        /// <returns>The arguments in order; never null.</returns>
        public static List<string> SplitArguments(string? arguments)
        {
            List<string> result = new List<string>();
            string text = arguments ?? string.Empty;
            StringBuilder current = new StringBuilder();
            char quote = '\0';
            bool inToken = false;

            foreach (char c in text)
            {
                if (quote != '\0')
                {
                    if (c == quote)
                    {
                        quote = '\0';
                    }
                    else
                    {
                        current.Append(c);
                    }

                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    quote = c;
                    inToken = true;
                    continue;
                }

                if (char.IsWhiteSpace(c))
                {
                    if (inToken)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                        inToken = false;
                    }

                    continue;
                }

                current.Append(c);
                inToken = true;
            }

            if (inToken)
            {
                result.Add(current.ToString());
            }

            return result;
        }

        #endregion
    }
}
