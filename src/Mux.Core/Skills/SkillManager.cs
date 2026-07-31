namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.RegularExpressions;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Performs the file-and-index operations behind the skills manager: creating a skill from a scaffold,
    /// enabling or disabling one in the index, removing one, and importing one from another directory. The
    /// UI and the CLI verb both drive this class, so their behavior cannot drift, and it is unit-tested
    /// directly rather than through the modal chain.
    /// </summary>
    public sealed class SkillManager
    {
        #region Private-Members

        private static readonly Regex _IdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

        private readonly string _SkillsDirectory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillManager"/> class.
        /// </summary>
        /// <param name="skillsDirectory">The directory that holds skills. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public SkillManager(string skillsDirectory)
        {
            _SkillsDirectory = skillsDirectory ?? throw new ArgumentNullException(nameof(skillsDirectory));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Indicates whether the supplied identifier is a valid skill id (lowercase, hyphen-separated).
        /// </summary>
        /// <param name="id">The identifier to check.</param>
        /// <returns><c>true</c> when the id is well-formed; otherwise <c>false</c>.</returns>
        public static bool IsValidId(string? id)
        {
            return id != null && _IdPattern.IsMatch(id);
        }

        /// <summary>
        /// Indicates whether a skill directory with the given id already exists.
        /// </summary>
        /// <param name="id">The skill id.</param>
        /// <returns><c>true</c> when a directory for the id exists; otherwise <c>false</c>.</returns>
        public bool Exists(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && Directory.Exists(Path.Combine(_SkillsDirectory, id));
        }

        /// <summary>
        /// Sets a skill's enablement in the index. Idempotent; creates the index row when absent.
        /// </summary>
        /// <param name="id">The skill id. Must not be null or blank.</param>
        /// <param name="enabled">The desired enablement.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null or blank.</exception>
        public void SetEnabled(string id, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A skill id is required.", nameof(id));
            }

            List<SkillIndexEntry> index = SettingsLoader.LoadSkillIndex();
            SkillIndexEntry? entry = index.Find(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                index.Add(new SkillIndexEntry { Id = id, Enabled = enabled });
            }
            else
            {
                entry.Enabled = enabled;
            }

            SettingsLoader.SaveSkillIndex(index);
        }

        /// <summary>
        /// Creates a new skill from a scaffold: validates the id, writes <c>SKILL.md</c>, and enables it in
        /// the index.
        /// </summary>
        /// <param name="scaffold">The scaffold inputs. Must not be null.</param>
        /// <returns>The absolute path of the created skill directory.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="scaffold"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the id is invalid.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a skill with the id already exists.</exception>
        public string Create(SkillScaffold scaffold)
        {
            if (scaffold == null) throw new ArgumentNullException(nameof(scaffold));
            if (!IsValidId(scaffold.Id))
            {
                throw new ArgumentException($"Skill id '{scaffold.Id}' must be lowercase and hyphen-separated.", nameof(scaffold));
            }

            string dir = Path.Combine(_SkillsDirectory, scaffold.Id);
            if (Directory.Exists(dir))
            {
                throw new InvalidOperationException($"A skill named '{scaffold.Id}' already exists.");
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), SkillScaffoldWriter.Build(scaffold));
            SetEnabled(scaffold.Id, true);
            return dir;
        }

        /// <summary>
        /// Removes a skill: deletes its directory and its index row.
        /// </summary>
        /// <param name="id">The skill id. Must not be null or blank.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null or blank.</exception>
        public void Remove(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A skill id is required.", nameof(id));
            }

            string dir = Path.Combine(_SkillsDirectory, id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }

            List<SkillIndexEntry> index = SettingsLoader.LoadSkillIndex();
            index.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            SettingsLoader.SaveSkillIndex(index);
        }

        /// <summary>
        /// Imports a skill from another directory after validating it, copying it under the target id.
        /// </summary>
        /// <param name="sourceDirectory">The source skill directory. Must not be null.</param>
        /// <param name="targetId">The id to import under, or null to use the source folder name.</param>
        /// <returns>The id the skill was imported under.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceDirectory"/> is null.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the source directory does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the source is not a valid skill or the target id already exists.</exception>
        /// <exception cref="ArgumentException">Thrown when the resolved target id is invalid.</exception>
        public string Import(string sourceDirectory, string? targetId)
        {
            if (sourceDirectory == null) throw new ArgumentNullException(nameof(sourceDirectory));
            if (!Directory.Exists(sourceDirectory))
            {
                throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");
            }

            string source = Path.GetFullPath(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string id = string.IsNullOrWhiteSpace(targetId) ? Path.GetFileName(source) : targetId!;
            if (!IsValidId(id))
            {
                throw new ArgumentException($"Import target id '{id}' must be lowercase and hyphen-separated.", nameof(targetId));
            }

            SkillLoader loader = new SkillLoader(Path.GetDirectoryName(source) ?? source);
            Skill candidate = loader.Load(source);
            if (!candidate.IsValid)
            {
                string reason = candidate.Validation.Errors.Count > 0 ? candidate.Validation.Errors[0] : "unknown validation error";
                throw new InvalidOperationException($"The source is not a valid skill: {reason}");
            }

            string target = Path.Combine(_SkillsDirectory, id);
            if (Directory.Exists(target))
            {
                throw new InvalidOperationException($"A skill named '{id}' already exists.");
            }

            CopyDirectory(source, target);
            SetEnabled(id, true);
            return id;
        }

        #endregion

        #region Private-Methods

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
            }

            foreach (string directory in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }

        #endregion
    }
}
