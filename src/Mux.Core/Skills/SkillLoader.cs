namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// Discovers and parses skills from a directory. Each immediate subfolder is one skill; its
    /// <c>SKILL.md</c> is split into frontmatter and body, the frontmatter is parsed into a manifest, the
    /// body's <c>id=</c>-tagged code blocks are extracted, and the result is validated. A skill that fails
    /// validation is still returned with <see cref="Skill.IsValid"/> false and populated errors, so callers
    /// can explain why it is broken rather than dropping it silently.
    /// </summary>
    public sealed class SkillLoader
    {
        #region Private-Members

        private const string SkillFileName = "SKILL.md";

        private static readonly Regex _SafeIdPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);
        private static readonly Regex _WellFormedIdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
        private static readonly Regex _FenceOpenPattern = new Regex("^```+\\s*(?<info>.*)$", RegexOptions.Compiled);
        private static readonly Regex _FenceClosePattern = new Regex("^```+\\s*$", RegexOptions.Compiled);
        private static readonly Regex _BlockIdPattern = new Regex("id=(?<id>[A-Za-z0-9._-]+)", RegexOptions.Compiled);

        private readonly string _SkillsDirectory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillLoader"/> class.
        /// </summary>
        /// <param name="skillsDirectory">The directory whose subfolders are skills. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public SkillLoader(string skillsDirectory)
        {
            _SkillsDirectory = skillsDirectory ?? throw new ArgumentNullException(nameof(skillsDirectory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Discovers every skill under the configured directory, parsing and validating each. Path-unsafe
        /// subfolder names (containing separators or a parent reference) are skipped entirely.
        /// </summary>
        /// <returns>The discovered skills, valid and invalid, ordered by folder name.</returns>
        public IReadOnlyList<Skill> Discover()
        {
            List<Skill> skills = new List<Skill>();
            if (!Directory.Exists(_SkillsDirectory))
            {
                return skills;
            }

            foreach (string directory in EnumerateSkillDirectories())
            {
                skills.Add(Load(directory));
            }

            return skills;
        }

        /// <summary>
        /// Asynchronously discovers every skill under the configured directory.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The discovered skills, valid and invalid, ordered by folder name.</returns>
        public async Task<IReadOnlyList<Skill>> DiscoverAsync(CancellationToken cancellationToken)
        {
            List<Skill> skills = new List<Skill>();
            if (!Directory.Exists(_SkillsDirectory))
            {
                return skills;
            }

            foreach (string directory in EnumerateSkillDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                skills.Add(await LoadAsync(directory, cancellationToken).ConfigureAwait(false));
            }

            return skills;
        }

        /// <summary>
        /// Loads and validates a single skill from its directory.
        /// </summary>
        /// <param name="skillDirectory">The skill's directory. Must not be null.</param>
        /// <returns>The loaded skill, valid or invalid.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillDirectory"/> is null.</exception>
        public Skill Load(string skillDirectory)
        {
            if (skillDirectory == null) throw new ArgumentNullException(nameof(skillDirectory));

            string path = Path.Combine(skillDirectory, SkillFileName);
            if (!File.Exists(path))
            {
                return BuildMissing(skillDirectory);
            }

            string content;
            try
            {
                content = File.ReadAllText(path);
            }
            catch (IOException ex)
            {
                return BuildUnreadable(skillDirectory, ex.Message);
            }

            return BuildSkill(skillDirectory, content);
        }

        /// <summary>
        /// Validates a parsed skill and returns the result, without touching disk beyond confirming that any
        /// bundled script paths exist and stay inside the skill directory.
        /// </summary>
        /// <param name="skill">The skill to validate. Must not be null.</param>
        /// <returns>A <see cref="SkillValidationResult"/> describing any problems.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skill"/> is null.</exception>
        public SkillValidationResult Validate(Skill skill)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));

            SkillValidationResult result = new SkillValidationResult();
            SkillManifest manifest = skill.Manifest;
            string folderId = Path.GetFileName(skill.DirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrWhiteSpace(manifest.Name))
            {
                result.Errors.Add("Frontmatter 'name' is required.");
            }
            else if (!string.Equals(manifest.Name, folderId, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add($"Frontmatter 'name' ('{manifest.Name}') must match the skill folder name ('{folderId}').");
            }
            else if (!_WellFormedIdPattern.IsMatch(manifest.Name))
            {
                result.Errors.Add($"Skill id '{manifest.Name}' must be lowercase and hyphen-separated.");
            }

            if (string.IsNullOrWhiteSpace(manifest.Description))
            {
                result.Errors.Add("Frontmatter 'description' is required.");
            }

            HashSet<string> commandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SkillCommand command in manifest.Commands)
            {
                ValidateCommand(skill, command, commandNames, result);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private async Task<Skill> LoadAsync(string skillDirectory, CancellationToken cancellationToken)
        {
            string path = Path.Combine(skillDirectory, SkillFileName);
            if (!File.Exists(path))
            {
                return BuildMissing(skillDirectory);
            }

            string content;
            try
            {
                content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                return BuildUnreadable(skillDirectory, ex.Message);
            }

            return BuildSkill(skillDirectory, content);
        }

        private IEnumerable<string> EnumerateSkillDirectories()
        {
            List<string> directories = new List<string>();
            foreach (string directory in Directory.EnumerateDirectories(_SkillsDirectory))
            {
                string name = Path.GetFileName(directory);
                if (IsSafeId(name))
                {
                    directories.Add(directory);
                }
            }

            directories.Sort(StringComparer.OrdinalIgnoreCase);
            return directories;
        }

        private Skill BuildSkill(string skillDirectory, string content)
        {
            SplitFrontmatter(content, out string frontmatter, out string body);
            SkillManifest manifest = SkillFrontmatterParser.Parse(frontmatter);

            Skill skill = new Skill
            {
                DirectoryPath = Path.GetFullPath(skillDirectory),
                Manifest = manifest,
                Body = body,
                CodeBlocks = ExtractCodeBlocks(body)
            };

            skill.Validation = Validate(skill);
            return skill;
        }

        private Skill BuildMissing(string skillDirectory)
        {
            Skill skill = new Skill { DirectoryPath = Path.GetFullPath(skillDirectory) };
            skill.Validation.Errors.Add($"{SkillFileName} not found in the skill directory.");
            return skill;
        }

        private Skill BuildUnreadable(string skillDirectory, string message)
        {
            Skill skill = new Skill { DirectoryPath = Path.GetFullPath(skillDirectory) };
            skill.Validation.Errors.Add($"{SkillFileName} could not be read: {message}");
            return skill;
        }

        private void ValidateCommand(Skill skill, SkillCommand command, HashSet<string> commandNames, SkillValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(command.Name))
            {
                result.Errors.Add("A command is missing its 'name'.");
                return;
            }

            if (!commandNames.Add(command.Name))
            {
                result.Errors.Add($"Command name '{command.Name}' is declared more than once.");
            }

            bool hasScript = !string.IsNullOrWhiteSpace(command.ScriptPath);
            bool hasBlock = !string.IsNullOrWhiteSpace(command.BlockId);
            if (hasScript == hasBlock)
            {
                result.Errors.Add($"Command '{command.Name}' must set exactly one of 'run' or 'block'.");
            }

            if (!SkillInterpreters.IsAllowed(command.Interpreter))
            {
                result.Errors.Add($"Command '{command.Name}' uses interpreter '{command.Interpreter}', which is not allowed.");
            }

            if (hasBlock && !skill.CodeBlocks.ContainsKey(command.BlockId!))
            {
                result.Errors.Add($"Command '{command.Name}' references block '{command.BlockId}', which does not exist in the body.");
            }

            if (hasScript)
            {
                ValidateScriptPath(skill, command, result);
            }
        }

        private void ValidateScriptPath(Skill skill, SkillCommand command, SkillValidationResult result)
        {
            string scriptPath = command.ScriptPath!;
            if (Path.IsPathRooted(scriptPath) || scriptPath.Contains("..", StringComparison.Ordinal))
            {
                result.Errors.Add($"Command '{command.Name}' script path '{scriptPath}' must be relative and stay inside the skill directory.");
                return;
            }

            string root = skill.DirectoryPath;
            string full = Path.GetFullPath(Path.Combine(root, scriptPath));
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add($"Command '{command.Name}' script path '{scriptPath}' escapes the skill directory.");
                return;
            }

            if (!File.Exists(full))
            {
                result.Errors.Add($"Command '{command.Name}' script '{scriptPath}' was not found.");
            }
        }

        private static void SplitFrontmatter(string content, out string frontmatter, out string body)
        {
            frontmatter = string.Empty;
            body = content ?? string.Empty;
            if (string.IsNullOrEmpty(content))
            {
                return;
            }

            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != "---")
            {
                return;
            }

            int closeIndex = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    closeIndex = i;
                    break;
                }
            }

            if (closeIndex < 0)
            {
                return;
            }

            frontmatter = string.Join("\n", lines, 1, closeIndex - 1);
            body = closeIndex + 1 < lines.Length ? string.Join("\n", lines, closeIndex + 1, lines.Length - closeIndex - 1) : string.Empty;
        }

        private Dictionary<string, string> ExtractCodeBlocks(string body)
        {
            Dictionary<string, string> blocks = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(body))
            {
                return blocks;
            }

            string[] lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int index = 0;
            while (index < lines.Length)
            {
                Match open = _FenceOpenPattern.Match(lines[index]);
                if (!open.Success)
                {
                    index++;
                    continue;
                }

                Match idMatch = _BlockIdPattern.Match(open.Groups["info"].Value);
                List<string> blockLines = new List<string>();
                int cursor = index + 1;
                bool closed = false;
                while (cursor < lines.Length)
                {
                    if (_FenceClosePattern.IsMatch(lines[cursor]))
                    {
                        closed = true;
                        break;
                    }

                    blockLines.Add(lines[cursor]);
                    cursor++;
                }

                if (idMatch.Success)
                {
                    string id = idMatch.Groups["id"].Value;
                    if (!blocks.ContainsKey(id))
                    {
                        blocks[id] = string.Join("\n", blockLines);
                    }
                }

                index = closed ? cursor + 1 : cursor;
            }

            return blocks;
        }

        private static bool IsSafeId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)
                || name.Contains("..", StringComparison.Ordinal)
                || name.IndexOf(Path.DirectorySeparatorChar) >= 0
                || name.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return false;
            }

            return _SafeIdPattern.IsMatch(name);
        }

        #endregion
    }
}
