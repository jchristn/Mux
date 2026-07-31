namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Parses the constrained YAML-style frontmatter of a <c>SKILL.md</c> into a <see cref="SkillManifest"/>.
    /// The supported subset is intentionally small — top-level scalars, booleans, inline or block string
    /// lists, and a <c>commands</c> block of maps — which keeps the parser dependency-free and lets it fail
    /// softly: malformed input yields a partial manifest rather than an exception, and the loader's
    /// validation decides whether the result is usable.
    /// </summary>
    public static class SkillFrontmatterParser
    {
        /// <summary>
        /// Parses frontmatter text (the content between the leading and trailing <c>---</c> fences) into a
        /// manifest. Never throws for malformed content; unrecognized lines are ignored.
        /// </summary>
        /// <param name="frontmatter">The frontmatter body. May be null or empty.</param>
        /// <returns>A populated <see cref="SkillManifest"/>; defaults apply to anything absent.</returns>
        public static SkillManifest Parse(string? frontmatter)
        {
            SkillManifest manifest = new SkillManifest();
            if (string.IsNullOrWhiteSpace(frontmatter))
            {
                return manifest;
            }

            string[] lines = frontmatter.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            // Block state: a non-empty list key means indented "- item" lines feed that string list; the
            // commands flag means indented lines build the current command.
            bool inCommands = false;
            string currentListKey = string.Empty;
            SkillCommand? currentCommand = null;

            foreach (string rawLine in lines)
            {
                string line = StripComment(rawLine);
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                int indent = CountIndent(line);
                string trimmed = line.Trim();

                if (indent == 0)
                {
                    inCommands = false;
                    currentListKey = string.Empty;
                    currentCommand = null;

                    if (!TrySplitKeyValue(trimmed, out string key, out string value))
                    {
                        continue;
                    }

                    HandleTopLevel(manifest, key, value, ref inCommands, ref currentListKey);
                    continue;
                }

                if (currentListKey.Length > 0 && trimmed.StartsWith("-", StringComparison.Ordinal))
                {
                    string item = StripQuotes(trimmed.Substring(1).Trim());
                    if (item.Length > 0)
                    {
                        AddToList(manifest, currentListKey, item);
                    }

                    continue;
                }

                if (inCommands)
                {
                    if (trimmed.StartsWith("-", StringComparison.Ordinal))
                    {
                        currentCommand = new SkillCommand();
                        manifest.Commands.Add(currentCommand);

                        string remainder = trimmed.Substring(1).Trim();
                        if (remainder.Length > 0 && TrySplitKeyValue(remainder, out string ck, out string cv))
                        {
                            ApplyCommandField(currentCommand, ck, cv);
                        }

                        continue;
                    }

                    if (currentCommand != null && TrySplitKeyValue(trimmed, out string fieldKey, out string fieldValue))
                    {
                        ApplyCommandField(currentCommand, fieldKey, fieldValue);
                    }
                }
            }

            return manifest;
        }

        private static void HandleTopLevel(SkillManifest manifest, string key, string value, ref bool inCommands, ref string currentListKey)
        {
            switch (key.ToLowerInvariant())
            {
                case "name":
                    manifest.Name = StripQuotes(value);
                    break;
                case "title":
                    manifest.Title = StripQuotes(value);
                    break;
                case "description":
                    manifest.Description = StripQuotes(value);
                    break;
                case "version":
                    manifest.Version = StripQuotes(value);
                    break;
                case "enabled":
                    manifest.Enabled = ParseBool(value, true);
                    break;
                case "mutating":
                    manifest.Mutating = ParseBool(value, true);
                    break;
                case "whentouse":
                    manifest.WhenToUse = StripQuotes(value);
                    break;
                case "allowedtools":
                    ApplyListKey(manifest, "allowedtools", value, ref currentListKey);
                    break;
                case "tags":
                    ApplyListKey(manifest, "tags", value, ref currentListKey);
                    break;
                case "commands":
                    inCommands = true;
                    currentListKey = string.Empty;
                    break;
                default:
                    break;
            }
        }

        private static void ApplyListKey(SkillManifest manifest, string listKey, string value, ref string currentListKey)
        {
            string inline = value.Trim();
            if (inline.StartsWith("[", StringComparison.Ordinal))
            {
                foreach (string item in ParseFlowList(inline))
                {
                    AddToList(manifest, listKey, item);
                }

                currentListKey = string.Empty;
                return;
            }

            currentListKey = listKey;
        }

        private static void AddToList(SkillManifest manifest, string listKey, string item)
        {
            if (string.Equals(listKey, "tags", StringComparison.OrdinalIgnoreCase))
            {
                manifest.Tags.Add(item);
            }
            else if (string.Equals(listKey, "allowedtools", StringComparison.OrdinalIgnoreCase))
            {
                manifest.AllowedTools.Add(item);
            }
        }

        private static void ApplyCommandField(SkillCommand command, string key, string value)
        {
            string clean = StripQuotes(value);
            switch (key.ToLowerInvariant())
            {
                case "name":
                    command.Name = clean;
                    break;
                case "description":
                    command.Description = clean;
                    break;
                case "run":
                    command.ScriptPath = clean;
                    break;
                case "block":
                    command.BlockId = clean;
                    break;
                case "interpreter":
                    command.Interpreter = clean;
                    break;
                case "timeoutms":
                    if (int.TryParse(clean, out int timeout))
                    {
                        command.TimeoutMs = timeout;
                    }

                    break;
                default:
                    break;
            }
        }

        private static IEnumerable<string> ParseFlowList(string inline)
        {
            string inner = inline.Trim();
            if (inner.StartsWith("[", StringComparison.Ordinal))
            {
                inner = inner.Substring(1);
            }

            if (inner.EndsWith("]", StringComparison.Ordinal))
            {
                inner = inner.Substring(0, inner.Length - 1);
            }

            foreach (string part in inner.Split(','))
            {
                string item = StripQuotes(part.Trim());
                if (item.Length > 0)
                {
                    yield return item;
                }
            }
        }

        private static bool TrySplitKeyValue(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                return false;
            }

            key = line.Substring(0, colon).Trim();
            value = line.Substring(colon + 1).Trim();
            return key.Length > 0;
        }

        private static string StripComment(string line)
        {
            // Drop a trailing comment introduced by " #" outside of quotes; keep an in-value '#' inside quotes.
            bool inSingle = false;
            bool inDouble = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                }
                else if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                }
                else if (c == '#' && !inSingle && !inDouble && (i == 0 || char.IsWhiteSpace(line[i - 1])))
                {
                    return line.Substring(0, i);
                }
            }

            return line;
        }

        private static string StripQuotes(string value)
        {
            string trimmed = value.Trim();
            if (trimmed.Length >= 2)
            {
                char first = trimmed[0];
                char last = trimmed[trimmed.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    return trimmed.Substring(1, trimmed.Length - 2);
                }
            }

            return trimmed;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            switch (StripQuotes(value).Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    return true;
                case "false":
                case "no":
                case "off":
                case "0":
                    return false;
                default:
                    return fallback;
            }
        }

        private static int CountIndent(string line)
        {
            int indent = 0;
            foreach (char c in line)
            {
                if (c == ' ')
                {
                    indent++;
                }
                else if (c == '\t')
                {
                    indent += 2;
                }
                else
                {
                    break;
                }
            }

            return indent;
        }
    }
}
