namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Checks applied to the skills imported from alirezarezvani/claude-skills (bundled defaults and optional packs):
    /// no em-dashes, every <c>${SKILL_DIR}</c> reference resolves, no bundled file is referenced without the
    /// placeholder, no instruction names only <c>CLAUDE.md</c>, and well-formed <c>pack.json</c> files. Each check
    /// returns the problems it found so tests can assert both clean trees and synthetic broken fixtures.
    /// </summary>
    public static class ImportedSkillChecks
    {
        #region Private-Members

        private static readonly Regex _Placeholder = new Regex(@"\$\{SKILL_DIR\}/([\w./-]+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex _BarePath = new Regex(@"(?:^|[\s(`""'])(?:\./)?((?:scripts|references|assets|templates)/[\w./-]+)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly char[] _Trailing = { '.', ',', ';', ':', ')', '`', '\'', '"' };

        #endregion

        #region Public-Members

        /// <summary>The em-dash character the repository's writing rules forbid.</summary>
        public const char EmDash = '\u2014';

        /// <summary>The top-level pack.json keys every pack must define.</summary>
        public static readonly IReadOnlyList<string> PackKeys = new[] { "id", "title", "description", "category", "source", "license" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lists the text files under a folder that contain an em-dash.
        /// </summary>
        /// <param name="directory">The folder.</param>
        /// <returns>Relative paths of offending files.</returns>
        public static List<string> FilesWithEmDash(string directory)
        {
            List<string> found = new List<string>();
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                byte[] bytes = File.ReadAllBytes(file);
                if (Array.IndexOf(bytes, (byte)0) >= 0)
                {
                    continue;
                }

                if (Encoding.UTF8.GetString(bytes).IndexOf(EmDash) >= 0)
                {
                    found.Add(Path.GetRelativePath(directory, file));
                }
            }

            return found;
        }

        /// <summary>
        /// Lists <c>${SKILL_DIR}/path</c> references whose target does not exist in the skill folder.
        /// </summary>
        /// <param name="skillDirectory">The skill folder.</param>
        /// <param name="text">The skill text.</param>
        /// <returns>The missing relative paths.</returns>
        public static List<string> MissingPlaceholderTargets(string skillDirectory, string text)
        {
            List<string> missing = new List<string>();
            foreach (Match match in _Placeholder.Matches(text ?? string.Empty))
            {
                string relative = match.Groups[1].Value.TrimEnd(_Trailing);
                if (!File.Exists(Path.Combine(skillDirectory, relative)) && !Directory.Exists(Path.Combine(skillDirectory, relative)))
                {
                    missing.Add(relative);
                }
            }

            return missing;
        }

        /// <summary>
        /// Lists bare <c>scripts/</c>, <c>references/</c>, <c>assets/</c>, or <c>templates/</c> paths that point at a file
        /// bundled with the skill but lack the <c>${SKILL_DIR}</c> placeholder, so the model would resolve them against
        /// the project directory instead of the skill folder.
        /// </summary>
        /// <param name="skillDirectory">The skill folder.</param>
        /// <param name="text">The skill text.</param>
        /// <returns>The bare relative paths.</returns>
        public static List<string> BareBundledPaths(string skillDirectory, string text)
        {
            List<string> bare = new List<string>();
            foreach (Match match in _BarePath.Matches(text ?? string.Empty))
            {
                string relative = match.Groups[1].Value.TrimEnd(_Trailing);
                if (File.Exists(Path.Combine(skillDirectory, relative)) || Directory.Exists(Path.Combine(skillDirectory, relative)))
                {
                    bare.Add(relative);
                }
            }

            return bare;
        }

        /// <summary>
        /// Lists lines that mention <c>CLAUDE.md</c> without naming mux's instruction files.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>The offending lines.</returns>
        public static List<string> ClaudeOnlyLines(string text)
        {
            List<string> lines = new List<string>();
            foreach (string line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Contains("CLAUDE.md", StringComparison.Ordinal) && !line.Contains("AGENTS.md", StringComparison.Ordinal))
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        /// <summary>
        /// Checks a pack folder's <c>pack.json</c>: present, valid JSON, every required key non-empty, the id equal to the
        /// folder name, and the license MIT.
        /// </summary>
        /// <param name="packDirectory">The pack folder.</param>
        /// <returns>The problems found.</returns>
        public static List<string> PackProblems(string packDirectory)
        {
            List<string> problems = new List<string>();
            string path = Path.Combine(packDirectory, "pack.json");
            if (!File.Exists(path))
            {
                problems.Add("pack.json is missing");
                return problems;
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object)
                    {
                        problems.Add("pack.json is not an object");
                        return problems;
                    }

                    foreach (string key in PackKeys)
                    {
                        if (!root.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                        {
                            problems.Add("pack.json is missing '" + key + "'");
                        }
                    }

                    string folder = Path.GetFileName(packDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (root.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String && id.GetString() != folder)
                    {
                        problems.Add("pack.json id '" + id.GetString() + "' does not match folder '" + folder + "'");
                    }

                    if (root.TryGetProperty("license", out JsonElement license) && license.ValueKind == JsonValueKind.String && license.GetString() != "MIT")
                    {
                        problems.Add("pack.json license is not MIT");
                    }
                }
            }
            catch (JsonException ex)
            {
                problems.Add("pack.json is not valid JSON: " + ex.Message);
            }

            return problems;
        }

        /// <summary>
        /// Finds the repository's <c>src</c> folder by walking up from the test binaries, or null when the tests run
        /// outside a checkout.
        /// </summary>
        /// <returns>The <c>src</c> folder, or null.</returns>
        public static string? FindSourceRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "Mux.Core", "Skills");
                if (Directory.Exists(Path.Combine(candidate, "Bundled")))
                {
                    return Path.Combine(directory.FullName, "src");
                }

                if (Directory.Exists(Path.Combine(directory.FullName, "Mux.Core", "Skills", "Bundled")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        #endregion
    }
}
