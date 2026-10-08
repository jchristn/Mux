namespace Mux.Core.Prompting
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Utility;

    /// <summary>
    /// Loads project instruction files for a working directory so every surface can put them in the system
    /// prompt. It walks from the working directory up to the repository root (or reads only the working
    /// directory when it is not inside a repository) and, at each level, takes the first of
    /// <see cref="FileNames"/> that exists. A user-level file (<see cref="UserInstructionsPath"/>) comes
    /// first, then outer directories before inner ones. When the combined size exceeds
    /// <see cref="MaxBytes"/>, the outermost files are dropped first and the innermost file is cut short only
    /// when it alone exceeds the cap. Unreadable files are skipped. Instances are safe to share once
    /// configured; loading does not mutate the instance.
    /// </summary>
    public sealed class ProjectInstructionsLoader
    {
        #region Private-Members

        private const string TruncationNote = "\n[truncated: this instruction file exceeded the projectInstructionsMaxBytes cap]";

        private List<string> _FileNames = new List<string> { "MUX.md", "AGENTS.md", "CLAUDE.md" };
        private int _MaxBytes = 32768;
        private string? _UserInstructionsPath = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectInstructionsLoader"/> class with the default
        /// file names and size cap and no user-level file.
        /// </summary>
        public ProjectInstructionsLoader()
        {
        }

        /// <summary>
        /// Creates a loader configured from settings, with the user-level file at <c>MUX.md</c> in the config
        /// directory. Returns null when project instructions are disabled (the setting is off or the cap is 0).
        /// </summary>
        /// <param name="settings">The settings to read. Null uses the defaults.</param>
        /// <returns>A configured loader, or null when disabled.</returns>
        public static ProjectInstructionsLoader? FromSettings(MuxSettings? settings)
        {
            MuxSettings effective = settings ?? new MuxSettings();
            if (!effective.ProjectInstructionsEnabled || effective.ProjectInstructionsMaxBytes <= 0)
            {
                return null;
            }

            string? userPath = null;
            try
            {
                userPath = Path.Combine(SettingsLoader.GetConfigDirectory(), "MUX.md");
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
            {
                userPath = null;
            }

            return new ProjectInstructionsLoader
            {
                MaxBytes = effective.ProjectInstructionsMaxBytes,
                UserInstructionsPath = userPath
            };
        }

        /// <summary>
        /// Loads the instructions for a working directory using settings, returning an empty result when
        /// project instructions are disabled. A convenience for surfaces that resolve a prompt per run.
        /// </summary>
        /// <param name="settings">The settings to read. Null uses the defaults.</param>
        /// <param name="workingDirectory">The working directory. Null or empty yields only the user file.</param>
        /// <returns>The loaded instructions; never null.</returns>
        public static ProjectInstructions LoadForSettings(MuxSettings? settings, string? workingDirectory)
        {
            ProjectInstructionsLoader? loader = FromSettings(settings);
            return loader == null ? new ProjectInstructions() : loader.Load(workingDirectory);
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The instruction file names checked at each directory level, in preference order; only the first
        /// one found in a directory is used. Defaults to <c>MUX.md</c>, <c>AGENTS.md</c>, <c>CLAUDE.md</c>.
        /// Never null; empty entries are ignored.
        /// </summary>
        public List<string> FileNames
        {
            get => _FileNames;
            set => _FileNames = value ?? new List<string>();
        }

        /// <summary>
        /// The maximum combined UTF-8 size of the loaded files, in bytes. Default 32768, minimum 0, maximum
        /// 1048576. Zero disables loading entirely.
        /// </summary>
        public int MaxBytes
        {
            get => _MaxBytes;
            set => _MaxBytes = Math.Clamp(value, 0, 1048576);
        }

        /// <summary>
        /// An optional user-level instruction file read before any project file (for example
        /// <c>~/.mux/MUX.md</c>). Null or a missing file is skipped.
        /// </summary>
        public string? UserInstructionsPath
        {
            get => _UserInstructionsPath;
            set => _UserInstructionsPath = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Loads and combines the instruction files for a working directory.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null or empty yields only the user file.</param>
        /// <returns>The loaded instructions; never null.</returns>
        public ProjectInstructions Load(string? workingDirectory)
        {
            List<string> candidates = FindCandidateFiles(workingDirectory);
            List<KeyValuePair<string, string>> contents = new List<KeyValuePair<string, string>>();
            foreach (string path in candidates)
            {
                string? text = TryRead(path);
                if (text != null)
                {
                    contents.Add(new KeyValuePair<string, string>(path, text));
                }
            }

            return Combine(contents, workingDirectory);
        }

        /// <summary>
        /// Asynchronously loads and combines the instruction files for a working directory.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null or empty yields only the user file.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The loaded instructions; never null.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the operation is canceled.</exception>
        public async Task<ProjectInstructions> LoadAsync(string? workingDirectory, CancellationToken cancellationToken)
        {
            List<string> candidates = FindCandidateFiles(workingDirectory);
            List<KeyValuePair<string, string>> contents = new List<KeyValuePair<string, string>>();
            foreach (string path in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
                    contents.Add(new KeyValuePair<string, string>(path, text));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Unreadable instruction files are skipped.
                }
            }

            return Combine(contents, workingDirectory);
        }

        /// <summary>
        /// Returns the instruction files that would be considered for a working directory, in precedence
        /// order (user file first, then outer to inner), without reading them.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null or empty yields only the user file.</param>
        /// <returns>The existing candidate files.</returns>
        public List<string> FindCandidateFiles(string? workingDirectory)
        {
            List<string> files = new List<string>();
            if (_MaxBytes <= 0)
            {
                return files;
            }

            if (_UserInstructionsPath != null && File.Exists(_UserInstructionsPath))
            {
                files.Add(Path.GetFullPath(_UserInstructionsPath));
            }

            foreach (string directory in EnumerateDirectoriesOuterToInner(workingDirectory))
            {
                foreach (string name in _FileNames)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    string path = Path.Combine(directory, name);
                    if (File.Exists(path))
                    {
                        string full = Path.GetFullPath(path);
                        if (!files.Contains(full))
                        {
                            files.Add(full);
                        }

                        break;
                    }
                }
            }

            return files;
        }

        #endregion

        #region Private-Methods

        private static List<string> EnumerateDirectoriesOuterToInner(string? workingDirectory)
        {
            List<string> directories = new List<string>();
            if (string.IsNullOrWhiteSpace(workingDirectory))
            {
                return directories;
            }

            string start;
            try
            {
                start = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workingDirectory));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return directories;
            }

            if (!Directory.Exists(start))
            {
                return directories;
            }

            string? root = RepositoryRootLocator.FindRepositoryRoot(start);
            if (root == null)
            {
                directories.Add(start);
                return directories;
            }

            string? current = start;
            while (!string.IsNullOrEmpty(current))
            {
                directories.Add(current);
                if (string.Equals(Path.TrimEndingDirectorySeparator(current), root, StringComparison.Ordinal))
                {
                    break;
                }

                current = Directory.GetParent(current)?.FullName;
            }

            directories.Reverse();
            return directories;
        }

        private ProjectInstructions Combine(List<KeyValuePair<string, string>> contents, string? workingDirectory)
        {
            ProjectInstructions result = new ProjectInstructions();
            if (contents.Count == 0 || _MaxBytes <= 0)
            {
                return result;
            }

            // Walk from the innermost (last) file outward, keeping files while they fit. Once one does not
            // fit, it and every file outside it are dropped, so the kept set is always the nearest files.
            List<KeyValuePair<string, string>> kept = new List<KeyValuePair<string, string>>();
            int budget = _MaxBytes;
            bool cutOff = false;
            for (int i = contents.Count - 1; i >= 0; i--)
            {
                KeyValuePair<string, string> entry = contents[i];
                string text = entry.Value.Replace("\r\n", "\n").Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                if (cutOff)
                {
                    result.DroppedSources.Insert(0, entry.Key);
                    continue;
                }

                int bytes = Encoding.UTF8.GetByteCount(text);
                if (bytes <= budget)
                {
                    kept.Insert(0, new KeyValuePair<string, string>(entry.Key, text));
                    budget -= bytes;
                    result.TotalBytes += bytes;
                    continue;
                }

                if (kept.Count == 0)
                {
                    string cut = TruncateToBytes(text, budget);
                    kept.Insert(0, new KeyValuePair<string, string>(entry.Key, cut + TruncationNote));
                    result.TotalBytes += Encoding.UTF8.GetByteCount(cut);
                    result.Truncated = true;
                    cutOff = true;
                    continue;
                }

                result.DroppedSources.Insert(0, entry.Key);
                result.Truncated = true;
                cutOff = true;
            }

            string? projectRoot = RepositoryRootLocator.FindProjectRoot(workingDirectory);
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in kept)
            {
                if (builder.Length > 0)
                {
                    builder.Append("\n\n");
                }

                builder.Append("### ").Append(DisplayPath(entry.Key, projectRoot)).Append('\n');
                builder.Append(entry.Value);
                result.Sources.Add(entry.Key);
            }

            result.Text = builder.ToString();
            return result;
        }

        private static string DisplayPath(string path, string? projectRoot)
        {
            if (projectRoot != null)
            {
                string relative = Path.GetRelativePath(projectRoot, path);
                if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
                {
                    return relative.Replace('\\', '/');
                }
            }

            return path;
        }

        private static string TruncateToBytes(string text, int maxBytes)
        {
            if (maxBytes <= 0)
            {
                return string.Empty;
            }

            int bytes = 0;
            int index = 0;
            while (index < text.Length)
            {
                int width = char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? 2 : 1;
                int charBytes = Encoding.UTF8.GetByteCount(text.ToCharArray(index, width));
                if (bytes + charBytes > maxBytes)
                {
                    break;
                }

                bytes += charBytes;
                index += width;
            }

            return text.Substring(0, index);
        }

        private static string? TryRead(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        #endregion
    }
}
