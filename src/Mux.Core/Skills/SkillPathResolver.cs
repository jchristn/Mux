namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Resolves a skill's own folder for the model. Skills (mux's and Claude-format ones) refer to files they bundle
    /// through a folder placeholder, most often <c>${SKILL_DIR}</c>; mux replaces every supported placeholder with the
    /// skill's absolute folder wherever the model reads a skill body (the <c>skill</c> tool, <c>/skill args</c>
    /// invocation, and <c>mux skill show</c>), and lists the files the skill bundles so instructions such as
    /// "run scripts/check.py" can be followed from any working directory.
    /// </summary>
    public static class SkillPathResolver
    {
        #region Private-Members

        // Braced and brace-style placeholders are replaced anywhere. Unbraced $SKILL_DIR / $SKILL_ROOT forms are replaced
        // only as whole tokens so a longer variable name that starts the same way is never touched.
        private static readonly string[] _Placeholders =
        {
            "${SKILL_DIR}", "${MUX_SKILL_DIR}", "${CLAUDE_SKILL_DIR}", "${CLAUDE_PLUGIN_ROOT}", "${SKILL_ROOT}",
            "{baseDir}", "{skill_path}", "{skillDir}"
        };

        private static readonly Regex _BareToken = new Regex(@"\$(SKILL_DIR|SKILL_ROOT|MUX_SKILL_DIR|CLAUDE_SKILL_DIR)(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);

        private static readonly HashSet<string> _SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "__pycache__", ".venv", "venv", "bin", "obj", ".pytest_cache", ".mypy_cache"
        };

        #endregion

        #region Public-Members

        /// <summary>The canonical placeholder for a skill's absolute folder.</summary>
        public const string Placeholder = "${SKILL_DIR}";

        /// <summary>The most files <see cref="ListFiles"/> returns by default.</summary>
        public const int DefaultMaxFiles = 200;

        /// <summary>The deepest folder level <see cref="ListFiles"/> descends into by default (the skill folder is 0).</summary>
        public const int DefaultMaxDepth = 4;

        /// <summary>
        /// The environment variable names a skill command receives holding the skill's folder.
        /// </summary>
        public static IReadOnlyList<string> EnvironmentVariables { get; } = new[] { "MUX_SKILL_DIR", "SKILL_DIR", "CLAUDE_SKILL_DIR" };

        /// <summary>
        /// Every placeholder <see cref="Substitute"/> replaces, for documentation and tests.
        /// </summary>
        public static IReadOnlyList<string> Placeholders { get; } = new List<string>(_Placeholders)
        {
            "$SKILL_DIR", "$SKILL_ROOT", "$MUX_SKILL_DIR", "$CLAUDE_SKILL_DIR"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replaces every folder placeholder in <paramref name="text"/> with <paramref name="skillDirectory"/>. Forward
        /// slashes are used so the result reads the same in prose and in shell snippets on every platform.
        /// </summary>
        /// <param name="text">The skill body (or any text from the skill). Null is treated as empty.</param>
        /// <param name="skillDirectory">The skill's absolute folder. Blank leaves the text unchanged.</param>
        /// <returns>The text with placeholders resolved.</returns>
        public static string Substitute(string? text, string? skillDirectory)
        {
            string value = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(skillDirectory) || value.Length == 0)
            {
                return value;
            }

            string folder = NormalizeFolder(skillDirectory!);
            foreach (string placeholder in _Placeholders)
            {
                value = value.Replace(placeholder, folder, StringComparison.Ordinal);
            }

            return _BareToken.Replace(value, folder);
        }

        /// <summary>
        /// Whether the text contains any folder placeholder.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>True when a placeholder is present.</returns>
        public static bool ContainsPlaceholder(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            foreach (string placeholder in _Placeholders)
            {
                if (text.Contains(placeholder, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return _BareToken.IsMatch(text);
        }

        /// <summary>
        /// The skill folder as shown to the model: absolute, with forward slashes and no trailing separator.
        /// </summary>
        /// <param name="skillDirectory">The folder.</param>
        /// <returns>The normalized folder.</returns>
        public static string NormalizeFolder(string skillDirectory)
        {
            string full = Path.GetFullPath(skillDirectory).Replace('\\', '/');
            return full.Length > 1 ? full.TrimEnd('/') : full;
        }

        /// <summary>
        /// Lists the files a skill bundles, as forward-slash paths relative to its folder, sorted, excluding
        /// <c>SKILL.md</c> at the top level, dot-files and dot-folders, and dependency or cache folders
        /// (<c>node_modules</c>, <c>__pycache__</c>, virtual environments). The walk stops at
        /// <paramref name="maxDepth"/> folder levels and <paramref name="maxFiles"/> files.
        /// </summary>
        /// <param name="skillDirectory">The skill folder.</param>
        /// <param name="truncated">True when files were left out because a limit was reached.</param>
        /// <param name="maxFiles">The most files to return (at least 1).</param>
        /// <param name="maxDepth">The deepest folder level to descend into (0 lists only the top level).</param>
        /// <returns>The relative paths.</returns>
        public static List<string> ListFiles(string? skillDirectory, out bool truncated, int maxFiles = DefaultMaxFiles, int maxDepth = DefaultMaxDepth)
        {
            truncated = false;
            List<string> files = new List<string>();
            if (string.IsNullOrWhiteSpace(skillDirectory) || !Directory.Exists(skillDirectory))
            {
                return files;
            }

            int limit = Math.Max(1, maxFiles);
            string root = Path.GetFullPath(skillDirectory!);
            bool hitLimit = false;
            Walk(root, root, 0, Math.Max(0, maxDepth), limit, files, ref hitLimit);
            files.Sort(StringComparer.Ordinal);
            truncated = hitLimit;
            return files;
        }

        #endregion

        #region Private-Methods

        private static void Walk(string root, string directory, int depth, int maxDepth, int limit, List<string> files, ref bool hitLimit)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFiles(directory);
            }
            catch (Exception)
            {
                return;
            }

            List<string> sorted = new List<string>(entries);
            sorted.Sort(StringComparer.Ordinal);
            foreach (string file in sorted)
            {
                string name = Path.GetFileName(file);
                if (name.StartsWith(".", StringComparison.Ordinal))
                {
                    continue;
                }

                if (depth == 0 && string.Equals(name, "SKILL.md", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (files.Count >= limit)
                {
                    hitLimit = true;
                    return;
                }

                files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }

            List<string> subdirectories;
            try
            {
                subdirectories = new List<string>(Directory.EnumerateDirectories(directory));
            }
            catch (Exception)
            {
                return;
            }

            subdirectories.Sort(StringComparer.Ordinal);
            foreach (string sub in subdirectories)
            {
                string name = Path.GetFileName(sub);
                if (name.StartsWith(".", StringComparison.Ordinal) || _SkippedDirectories.Contains(name))
                {
                    continue;
                }

                if (depth >= maxDepth)
                {
                    hitLimit = true;
                    continue;
                }

                Walk(root, sub, depth + 1, maxDepth, limit, files, ref hitLimit);
                if (files.Count >= limit && hitLimit)
                {
                    return;
                }
            }
        }

        #endregion
    }
}
