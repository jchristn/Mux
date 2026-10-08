namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Reads and writes per-project trust decisions for checked-in skills, stored as JSON (by default in
    /// <c>~/.mux/trusted-projects.json</c>). A project skill with runnable commands is code from the
    /// repository, so it loads only after the user trusts that project root. Reads never throw: a missing or
    /// malformed file reads as "no decisions". Thread-safe: every operation is serialized on an instance lock,
    /// and writes replace the file atomically.
    /// </summary>
    public sealed class ProjectTrustStore
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static readonly JsonSerializerOptions _WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly object _Sync = new object();
        private readonly string _FilePath;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectTrustStore"/> class.
        /// </summary>
        /// <param name="filePath">The JSON file holding the decisions. Must not be null or empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="filePath"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="filePath"/> is empty.</exception>
        public ProjectTrustStore(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("The trust store path must not be empty.", nameof(filePath));
            _FilePath = filePath;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The JSON file this store reads and writes.
        /// </summary>
        public string FilePath => _FilePath;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the recorded trust level for a project root, or <see cref="ProjectTrustLevelEnum.Unknown"/>
        /// when none is recorded.
        /// </summary>
        /// <param name="projectRoot">The project root. Null or empty returns <c>Unknown</c>.</param>
        /// <returns>The recorded level.</returns>
        public ProjectTrustLevelEnum GetLevel(string? projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return ProjectTrustLevelEnum.Unknown;
            }

            string key = Normalize(projectRoot);
            lock (_Sync)
            {
                foreach (ProjectTrustEntry entry in ReadNoLock().Projects)
                {
                    if (PathsEqual(Normalize(entry.Root), key))
                    {
                        return TryParseLevel(entry.Level, out ProjectTrustLevelEnum level) ? level : ProjectTrustLevelEnum.Unknown;
                    }
                }
            }

            return ProjectTrustLevelEnum.Unknown;
        }

        /// <summary>
        /// Records a trust level for a project root, replacing any earlier decision. Setting
        /// <see cref="ProjectTrustLevelEnum.Unknown"/> removes the decision.
        /// </summary>
        /// <param name="projectRoot">The project root. Must not be null or empty.</param>
        /// <param name="level">The level to record.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="projectRoot"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="projectRoot"/> is empty.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public void SetLevel(string projectRoot, ProjectTrustLevelEnum level)
        {
            if (projectRoot == null) throw new ArgumentNullException(nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("The project root must not be empty.", nameof(projectRoot));

            string key = Normalize(projectRoot);
            lock (_Sync)
            {
                ProjectTrustFile file = ReadNoLock();
                file.Projects.RemoveAll((ProjectTrustEntry e) => PathsEqual(Normalize(e.Root), key));
                if (level != ProjectTrustLevelEnum.Unknown)
                {
                    file.Projects.Add(new ProjectTrustEntry
                    {
                        Root = key,
                        Level = ToWireName(level),
                        DecidedUtc = DateTime.UtcNow
                    });
                }

                string? directory = Path.GetDirectoryName(_FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string temp = _FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(file, _WriteOptions));
                File.Move(temp, _FilePath, true);
            }
        }

        /// <summary>
        /// Returns the wire name for a trust level: <c>all</c>, <c>playbooks</c>, <c>ignore</c>, or
        /// <c>unknown</c>.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <returns>The wire name.</returns>
        public static string ToWireName(ProjectTrustLevelEnum level)
        {
            return level switch
            {
                ProjectTrustLevelEnum.All => "all",
                ProjectTrustLevelEnum.PlaybooksOnly => "playbooks",
                ProjectTrustLevelEnum.Ignore => "ignore",
                _ => "unknown"
            };
        }

        /// <summary>
        /// Parses a trust level from its wire name or a common synonym (<c>trust</c>, <c>yes</c>,
        /// <c>playbooks-only</c>, <c>none</c>, <c>no</c>, <c>untrust</c>, <c>reset</c>).
        /// </summary>
        /// <param name="value">The text to parse.</param>
        /// <param name="level">The parsed level when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> when the text named a level.</returns>
        public static bool TryParseLevel(string? value, out ProjectTrustLevelEnum level)
        {
            string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("_", "-");
            switch (normalized)
            {
                case "all":
                case "trust":
                case "yes":
                case "true":
                    level = ProjectTrustLevelEnum.All;
                    return true;
                case "playbooks":
                case "playbooks-only":
                case "playbook":
                    level = ProjectTrustLevelEnum.PlaybooksOnly;
                    return true;
                case "ignore":
                case "none":
                case "no":
                case "false":
                    level = ProjectTrustLevelEnum.Ignore;
                    return true;
                case "unknown":
                case "untrust":
                case "reset":
                    level = ProjectTrustLevelEnum.Unknown;
                    return true;
                default:
                    level = ProjectTrustLevelEnum.Unknown;
                    return false;
            }
        }

        #endregion

        #region Private-Methods

        private ProjectTrustFile ReadNoLock()
        {
            try
            {
                if (!File.Exists(_FilePath))
                {
                    return new ProjectTrustFile();
                }

                ProjectTrustFile? file = JsonSerializer.Deserialize<ProjectTrustFile>(File.ReadAllText(_FilePath), _ReadOptions);
                return file ?? new ProjectTrustFile();
            }
            catch (JsonException)
            {
                return new ProjectTrustFile();
            }
            catch (IOException)
            {
                return new ProjectTrustFile();
            }
            catch (UnauthorizedAccessException)
            {
                return new ProjectTrustFile();
            }
        }

        private static string Normalize(string path)
        {
            try
            {
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return path.Trim();
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left, right, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
