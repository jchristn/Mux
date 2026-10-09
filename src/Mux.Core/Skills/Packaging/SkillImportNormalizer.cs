namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Normalizes Claude-format skill text for mux. Pure (text in, text out, plus a list of changes and things a
    /// person should look at), so the importer, the pack build, and tests share one set of rules:
    /// <list type="bullet">
    /// <item>Em-dashes and en-dashes are removed: a spaced dash after a short lead-in (a heading, list label, or a few
    /// words) becomes a colon, other spaced dashes become commas, a dash between digits becomes a hyphen, and scripts
    /// get a plain hyphen. No U+2014 or U+2013 survives.</item>
    /// <item>Folder placeholders (<c>${CLAUDE_SKILL_DIR}</c>, <c>${CLAUDE_PLUGIN_ROOT}</c>, <c>{baseDir}</c>,
    /// <c>{skill_path}</c>, <c>$SKILL_ROOT</c>) become <c>${SKILL_DIR}</c>, and references to the skill's own
    /// <c>scripts/</c>, <c>references/</c>, <c>assets/</c>, or <c>templates/</c> folders are anchored to it.</item>
    /// <item>Claude Code references are rewritten to their mux equivalents (<c>CLAUDE.md</c>, <c>~/.claude</c>,
    /// <c>claude -p</c>, built-in tool names); ones with no equivalent are flagged.</item>
    /// <item>Frontmatter gains <c>category:</c>, <c>source:</c>, and <c>license:</c> when they are missing.</item>
    /// </list>
    /// </summary>
    public static class SkillImportNormalizer
    {
        #region Private-Members

        private const char EmDash = '\u2014';
        private const char EnDash = '\u2013';
        private const string InstructionPhrase = "the project instruction file (MUX.md, AGENTS.md, or CLAUDE.md)";
        private const string InstructionToken = "\u0001MUXINSTR\u0001";

        private static readonly string[] _FolderAliases = { "${CLAUDE_SKILL_DIR}", "${CLAUDE_PLUGIN_ROOT}", "${SKILL_ROOT}", "${MUX_SKILL_DIR}", "{baseDir}", "{skill_path}", "{skillDir}" };

        private static readonly Regex _BareAlias = new Regex(@"\$(SKILL_ROOT|CLAUDE_SKILL_DIR|CLAUDE_PLUGIN_ROOT)(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

        private static readonly string[] _Folders = { "scripts", "references", "assets", "templates" };

        // "<runner> scripts/x.py" or "<runner> ./scripts/x.py" -> runner "${SKILL_DIR}/scripts/x.py" (quoted).
        private static readonly Regex _RunnerPath = new Regex(@"\b(python3?|py|bash|sh|zsh|node|pwsh|powershell|ruby|perl|deno|bun|tsx|ts-node|uv run|npx)\s+(?:\./)?(scripts|references|assets|templates)/([^\s`'"")\]]+)", RegexOptions.CultureInvariant);

        // A bare folder reference not already anchored: not preceded by a path or variable character.
        private static readonly Regex _FolderPath = new Regex(@"(?<![\w/$.{}\-~\\])(?:\./)?(scripts|references|assets|templates)/(?=[\w.\-*])", RegexOptions.CultureInvariant);

        private static readonly Regex _ClaudeMd = new Regex(@"(?<![\w/\\.\-])CLAUDE\.md\b", RegexOptions.CultureInvariant);

        private static readonly Regex _ClaudeHome = new Regex(@"~/\.claude\b", RegexOptions.CultureInvariant);

        private static readonly Regex _ClaudePrint = new Regex(@"\bclaude\s+(?:-p|--print)\b", RegexOptions.CultureInvariant);

        private static readonly Regex _ToolPhrase = new Regex(@"\b(Bash|Read|Write|Edit|MultiEdit|Grep|Glob|LS|WebFetch|WebSearch|Task|Agent|TodoWrite|AskUserQuestion|ExitPlanMode|Skill|NotebookRead)\s+tool\b", RegexOptions.CultureInvariant);

        private static readonly Regex _DistinctTool = new Regex(@"\b(TodoWrite|WebFetch|WebSearch|AskUserQuestion|ExitPlanMode)\b", RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> _ToolMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Bash"] = "run_process",
            ["Read"] = "read_file",
            ["NotebookRead"] = "read_file",
            ["Write"] = "write_file",
            ["Edit"] = "edit_file",
            ["MultiEdit"] = "multi_edit",
            ["Grep"] = "grep",
            ["Glob"] = "glob",
            ["LS"] = "list_directory",
            ["WebFetch"] = "web_retrieve",
            ["WebSearch"] = "web_search",
            ["Task"] = "spawn_subagent",
            ["Agent"] = "spawn_subagent",
            ["TodoWrite"] = "plan_tasks",
            ["AskUserQuestion"] = "ask_user",
            ["ExitPlanMode"] = "exit_plan",
            ["Skill"] = "skill"
        };

        private static readonly Regex _Unsupported = new Regex(@"\b(?:CronCreate|CronDelete|CronList|ScheduleWakeup|NotebookEdit|ToolSearch|SlashCommand|Monitor tool|Workflow tool|claude mcp|CLAUDE_PLUGIN_DATA)\b|(?<![\w/])/(?:plugin|agents)\b|\.claude/(?:settings\.json|agents|commands)", RegexOptions.CultureInvariant);

        private static readonly Regex _LeadingMarker = new Regex(@"^\s*(?:#{1,6}\s+|[-*+]\s+|\d+[.)]\s+|>\s*|\|\s*)?", RegexOptions.CultureInvariant);

        private static readonly string[][] _CategoryKeywords =
        {
            new[] { "marketing", "marketing" }, new[] { "seo", "marketing" }, new[] { "content", "marketing" },
            new[] { "c-level", "business" }, new[] { "executive", "business" }, new[] { "finance", "business" }, new[] { "business", "business" },
            new[] { "compliance", "compliance" }, new[] { "ra-qm", "compliance" }, new[] { "regulatory", "compliance" }, new[] { "iso", "compliance" },
            new[] { "product", "product" }, new[] { "project-management", "productivity" }, new[] { "productivity", "productivity" },
            new[] { "research", "research" }, new[] { "security", "security" }, new[] { "data", "data" }, new[] { "docs", "docs" },
            new[] { "test", "testing" }, new[] { "playwright", "testing" }, new[] { "engineering", "engineering" }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Normalizes a skill's Markdown (its <c>SKILL.md</c> or a reference file).
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="skillFolders">The skill's own top-level folders (for example <c>scripts</c>); references to them are anchored to <c>${SKILL_DIR}</c>. Null anchors none.</param>
        /// <param name="changes">What was changed, one entry per rule with a count.</param>
        /// <param name="flags">Things with no mux equivalent that a person should review, with line numbers.</param>
        /// <returns>The normalized text.</returns>
        public static string NormalizeMarkdown(string text, IReadOnlyCollection<string>? skillFolders, out List<string> changes, out List<string> flags)
        {
            changes = new List<string>();
            flags = new List<string>();
            string value = (text ?? string.Empty).Replace("\r\n", "\n");

            int placeholders = 0;
            foreach (string alias in _FolderAliases)
            {
                placeholders += Count(value, alias);
                value = value.Replace(alias, SkillPathResolver.Placeholder, StringComparison.Ordinal);
            }

            placeholders += _BareAlias.Matches(value).Count;
            value = _BareAlias.Replace(value, SkillPathResolver.Placeholder);
            Note(changes, "folder placeholders rewritten to ${SKILL_DIR}", placeholders);

            HashSet<string> folders = new HashSet<string>(skillFolders ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            int anchored = 0;
            value = _RunnerPath.Replace(value, (Match m) =>
            {
                if (!folders.Contains(m.Groups[2].Value)) return m.Value;
                anchored++;
                return m.Groups[1].Value + " \"" + SkillPathResolver.Placeholder + "/" + m.Groups[2].Value + "/" + m.Groups[3].Value + "\"";
            });
            value = _FolderPath.Replace(value, (Match m) =>
            {
                if (!folders.Contains(m.Groups[1].Value)) return m.Value;
                anchored++;
                return SkillPathResolver.Placeholder + "/" + m.Groups[1].Value + "/";
            });
            Note(changes, "bundled file paths anchored to ${SKILL_DIR}", anchored);

            value = RewriteClaudeReferences(value, changes, flags);

            int dashes = CountDashes(value);
            value = ReplaceDashes(value, prose: true);
            Note(changes, "em-dashes and en-dashes replaced", dashes);
            return value;
        }

        /// <summary>
        /// Normalizes a bundled script or data file: only dashes change (to a plain hyphen), so code keeps its meaning.
        /// </summary>
        /// <param name="text">The file text.</param>
        /// <param name="changed">How many dashes were replaced.</param>
        /// <returns>The normalized text.</returns>
        public static string NormalizeScript(string text, out int changed)
        {
            string value = text ?? string.Empty;
            changed = CountDashes(value);
            return value.Replace(EmDash, '-').Replace(EnDash, '-');
        }

        /// <summary>
        /// Replaces em-dashes and en-dashes. In prose: a spaced dash after a short lead-in becomes ": ", other spaced
        /// dashes ", ", a dash between digits "-", a trailing dash ":", and a leading dash (an attribution) is dropped.
        /// Outside prose every dash becomes "-". The result never contains U+2014 or U+2013.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="prose">True for Markdown prose, false for code.</param>
        /// <returns>The text without em-dashes or en-dashes.</returns>
        public static string ReplaceDashes(string text, bool prose)
        {
            string value = text ?? string.Empty;
            if (!prose)
            {
                return value.Replace(EmDash, '-').Replace(EnDash, '-');
            }

            string[] lines = value.Split('\n');
            bool inFence = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                if (line.IndexOf(EmDash) < 0 && line.IndexOf(EnDash) < 0)
                {
                    continue;
                }

                lines[i] = inFence ? line.Replace(EmDash, '-').Replace(EnDash, '-') : ReplaceDashesInLine(line);
            }

            string result = string.Join("\n", lines);
            return result.Replace(EmDash.ToString(), ", ").Replace(EnDash.ToString(), "-");
        }

        /// <summary>
        /// Adds <c>category:</c>, <c>source:</c>, and <c>license:</c> to the frontmatter when they are missing (and
        /// <c>name:</c> and <c>description:</c> when the file has no frontmatter at all).
        /// </summary>
        /// <param name="text">The <c>SKILL.md</c> text.</param>
        /// <param name="skillId">The skill id, used for a missing name.</param>
        /// <param name="category">The category to add, or null to leave it out.</param>
        /// <param name="source">The source to add, or null.</param>
        /// <param name="license">The license to add, or null.</param>
        /// <param name="changes">What was added.</param>
        /// <returns>The text with the fields present.</returns>
        public static string EnsureFrontmatter(string text, string skillId, string? category, string? source, string? license, out List<string> changes)
        {
            changes = new List<string>();
            string value = (text ?? string.Empty).Replace("\r\n", "\n");
            int end = FrontmatterEnd(value);
            string frontmatter;
            string body;
            if (end < 0)
            {
                body = value;
                frontmatter = "name: " + skillId + "\ndescription: " + Yaml(FirstSentence(body, skillId)) + "\n";
                changes.Add("frontmatter created (name, description)");
            }
            else
            {
                frontmatter = value.Substring(4, end - 4);
                body = value.Substring(end + 4).TrimStart('\n');
            }

            if (!frontmatter.EndsWith("\n", StringComparison.Ordinal)) frontmatter += "\n";
            frontmatter = AddField(frontmatter, "category", category, changes);
            frontmatter = AddField(frontmatter, "source", source, changes);
            frontmatter = AddField(frontmatter, "license", license, changes);
            return "---\n" + frontmatter + "---\n\n" + body;
        }

        /// <summary>
        /// Reads a top-level frontmatter value (for example <c>category</c>) from a <c>SKILL.md</c>.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="key">The key (case-insensitive).</param>
        /// <returns>The value without quotes, or null.</returns>
        public static string? ReadFrontmatterValue(string text, string key)
        {
            string value = (text ?? string.Empty).Replace("\r\n", "\n");
            int end = FrontmatterEnd(value);
            if (end < 0) return null;
            foreach (string line in value.Substring(4, end - 4).Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0 || char.IsWhiteSpace(line[0])) continue;
                if (string.Equals(line.Substring(0, colon).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    string raw = line.Substring(colon + 1).Trim();
                    return raw.Length >= 2 && (raw[0] == '"' || raw[0] == '\'') && raw[raw.Length - 1] == raw[0] ? raw.Substring(1, raw.Length - 2) : raw;
                }
            }

            return null;
        }

        /// <summary>
        /// Infers a mux category from a pack id, a source folder path, and the skill's text, falling back to
        /// <c>general</c>.
        /// </summary>
        /// <param name="pack">The pack the skill is imported into, or null.</param>
        /// <param name="sourcePath">The skill's folder path in its source repository, or null.</param>
        /// <param name="description">The skill's description, or null.</param>
        /// <returns>A kebab-case category.</returns>
        public static string InferCategory(string? pack, string? sourcePath, string? description)
        {
            string? fromPack = SkillCategories.Normalize(pack);
            if (!string.IsNullOrEmpty(fromPack)) return fromPack!;
            string path = (sourcePath ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
            foreach (string[] rule in _CategoryKeywords)
            {
                if (path.Contains(rule[0], StringComparison.Ordinal)) return rule[1];
            }

            string text = (description ?? string.Empty).ToLowerInvariant();
            foreach (string[] rule in _CategoryKeywords)
            {
                if (Regex.IsMatch(text, @"\b" + Regex.Escape(rule[0]) + @"\b", RegexOptions.CultureInvariant)) return rule[1];
            }

            return SkillCategories.General;
        }

        #endregion

        #region Private-Methods

        private static string RewriteClaudeReferences(string value, List<string> changes, List<string> flags)
        {
            string[] lines = value.Split('\n');
            bool inFence = false;
            int instructions = 0;
            int homes = 0;
            int prints = 0;
            int tools = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                {
                    inFence = !inFence;
                    continue;
                }

                homes += _ClaudeHome.Matches(line).Count;
                line = _ClaudeHome.Replace(line, "~/.mux");
                prints += _ClaudePrint.Matches(line).Count;
                line = _ClaudePrint.Replace(line, "mux print");

                if (inFence)
                {
                    if (_ClaudeMd.IsMatch(line)) flags.Add("line " + (i + 1) + ": CLAUDE.md in a code block (mux also reads MUX.md and AGENTS.md)");
                }
                else
                {
                    line = line.Replace(InstructionPhrase, InstructionToken, StringComparison.Ordinal);
                    line = RewriteOutsideBackticks(line, (string segment, bool code) =>
                    {
                        if (code)
                        {
                            int before = _ClaudeMd.Matches(segment).Count;
                            instructions += before;
                            segment = _ClaudeMd.Replace(segment, "MUX.md");
                            return segment;
                        }

                        instructions += _ClaudeMd.Matches(segment).Count;
                        segment = _ClaudeMd.Replace(segment, InstructionPhrase);
                        tools += _ToolPhrase.Matches(segment).Count;
                        segment = _ToolPhrase.Replace(segment, (Match m) => _ToolMap[m.Groups[1].Value] + " tool");
                        tools += _DistinctTool.Matches(segment).Count;
                        segment = _DistinctTool.Replace(segment, (Match m) => _ToolMap[m.Groups[1].Value]);
                        return segment;
                    });
                    line = line.Replace(InstructionToken, InstructionPhrase, StringComparison.Ordinal);
                }

                foreach (Match match in _Unsupported.Matches(line))
                {
                    flags.Add("line " + (i + 1) + ": " + match.Value + " has no direct mux equivalent");
                }

                lines[i] = line;
            }

            Note(changes, "CLAUDE.md references rewritten to the project instruction file", instructions);
            Note(changes, "~/.claude paths rewritten to ~/.mux", homes);
            Note(changes, "claude -p rewritten to mux print", prints);
            Note(changes, "Claude Code tool names rewritten to mux tools", tools);
            return string.Join("\n", lines);
        }

        private static string RewriteOutsideBackticks(string line, Func<string, bool, string> rewrite)
        {
            StringBuilder builder = new StringBuilder();
            bool code = false;
            int start = 0;
            for (int i = 0; i <= line.Length; i++)
            {
                if (i == line.Length || line[i] == '`')
                {
                    builder.Append(rewrite(line.Substring(start, i - start), code));
                    if (i < line.Length) builder.Append('`');
                    code = !code;
                    start = i + 1;
                }
            }

            return builder.ToString();
        }

        private static string ReplaceDashesInLine(string line)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c != EmDash && c != EnDash)
                {
                    builder.Append(c);
                    continue;
                }

                char before = PreviousNonSpace(line, i);
                char after = NextNonSpace(line, i);
                bool spacedBefore = i > 0 && char.IsWhiteSpace(line[i - 1]);
                bool spacedAfter = i + 1 < line.Length && char.IsWhiteSpace(line[i + 1]);

                if (char.IsDigit(before) && char.IsDigit(after))
                {
                    TrimTrailingSpace(builder);
                    builder.Append('-');
                    i = SkipSpaces(line, i);
                    continue;
                }

                string left = builder.ToString();
                if (left.Trim().Length == 0 || IsOnlyMarker(left))
                {
                    // A leading dash (an attribution or an unusual bullet): drop it.
                    i = SkipSpaces(line, i);
                    continue;
                }

                if (after == '\0')
                {
                    TrimTrailingSpace(builder);
                    builder.Append(':');
                    i = SkipSpaces(line, i);
                    continue;
                }

                if (c == EnDash && !spacedBefore && !spacedAfter)
                {
                    builder.Append('-');
                    continue;
                }

                TrimTrailingSpace(builder);
                bool shortLead = IsShortLead(left);
                builder.Append(shortLead ? ": " : ", ");
                i = SkipSpaces(line, i);
            }

            return builder.ToString();
        }

        private static bool IsShortLead(string left)
        {
            string lead = _LeadingMarker.Replace(left, string.Empty).Trim();
            int lastBreak = Math.Max(lead.LastIndexOf(": ", StringComparison.Ordinal), Math.Max(lead.LastIndexOf(". ", StringComparison.Ordinal), lead.LastIndexOf("| ", StringComparison.Ordinal)));
            if (lead.Contains(':', StringComparison.Ordinal) && lastBreak < 0)
            {
                return false;
            }

            if (lastBreak >= 0) lead = lead.Substring(lastBreak + 2);
            lead = lead.Trim('*', '_', '`', ' ');
            if (lead.Length == 0) return false;
            int words = lead.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return words <= 5;
        }

        private static bool IsOnlyMarker(string left)
        {
            return _LeadingMarker.Replace(left, string.Empty).Trim().Length == 0;
        }

        private static char PreviousNonSpace(string line, int index)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (!char.IsWhiteSpace(line[i])) return line[i];
            }

            return '\0';
        }

        private static char NextNonSpace(string line, int index)
        {
            for (int i = index + 1; i < line.Length; i++)
            {
                if (!char.IsWhiteSpace(line[i])) return line[i];
            }

            return '\0';
        }

        private static int SkipSpaces(string line, int index)
        {
            int i = index;
            while (i + 1 < line.Length && line[i + 1] == ' ') i++;
            return i;
        }

        private static void TrimTrailingSpace(StringBuilder builder)
        {
            while (builder.Length > 0 && builder[builder.Length - 1] == ' ') builder.Length--;
        }

        private static int CountDashes(string value)
        {
            int count = 0;
            foreach (char c in value)
            {
                if (c == EmDash || c == EnDash) count++;
            }

            return count;
        }

        private static int Count(string value, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static void Note(List<string> changes, string rule, int count)
        {
            if (count > 0) changes.Add(rule + " (" + count.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static int FrontmatterEnd(string value)
        {
            if (!value.StartsWith("---\n", StringComparison.Ordinal)) return -1;
            int end = value.IndexOf("\n---", 3, StringComparison.Ordinal);
            if (end < 0) return -1;
            int afterMarker = end + 4;
            if (afterMarker < value.Length && value[afterMarker] != '\n') return -1;
            return end + 1;
        }

        private static string AddField(string frontmatter, string key, string? value, List<string> changes)
        {
            if (string.IsNullOrWhiteSpace(value)) return frontmatter;
            foreach (string line in frontmatter.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon > 0 && !char.IsWhiteSpace(line[0]) && string.Equals(line.Substring(0, colon).Trim(), key, StringComparison.OrdinalIgnoreCase))
                {
                    return frontmatter;
                }
            }

            changes.Add(key + ": " + value!.Trim() + " added");
            return frontmatter + key + ": " + Yaml(value.Trim()) + "\n";
        }

        private static string Yaml(string value)
        {
            string clean = ReplaceDashes(value.Replace("\n", " "), prose: true);
            bool needsQuotes = clean.IndexOfAny(new[] { ':', '#', '"', '\'', '[', ']', '{', '}', ',', '&', '*', '!', '|', '>', '%', '@', '`' }) >= 0;
            return needsQuotes ? "\"" + clean.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : clean;
        }

        private static string FirstSentence(string body, string fallback)
        {
            foreach (string raw in body.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                return line.Length > 200 ? line.Substring(0, 200) : line;
            }

            return "Imported skill " + fallback + ".";
        }

        #endregion
    }
}
