namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Writes a skill folder's files to disk: refuses paths that escape the folder, writes bytes as-is, and marks
    /// scripts executable on Unix (files under <c>scripts/</c>, <c>.sh</c> and <c>.py</c> files, and files that start
    /// with <c>#!</c>), so bundled and pack skills run like a cloned skill folder would.
    /// </summary>
    public static class SkillFileWriter
    {
        #region Public-Methods

        /// <summary>
        /// Writes every file into <paramref name="directory"/>, creating folders as needed.
        /// </summary>
        /// <param name="directory">The skill folder.</param>
        /// <param name="files">The files by forward-slash relative path.</param>
        /// <param name="skillId">The skill id, used in error messages.</param>
        /// <exception cref="InvalidOperationException">Thrown when a path escapes the skill folder.</exception>
        public static void WriteAll(string directory, IReadOnlyDictionary<string, byte[]> files, string skillId)
        {
            Directory.CreateDirectory(directory);
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                string target = Path.GetFullPath(Path.Combine(directory, file.Key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Skill '" + skillId + "' file '" + file.Key + "' escapes the skill directory.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, file.Value);
                if (ShouldBeExecutable(file.Key, file.Value))
                {
                    MakeExecutable(target);
                }
            }
        }

        /// <summary>
        /// Whether a file should be executable: anything under <c>scripts/</c>, <c>.sh</c> and <c>.py</c> files, and
        /// files whose content starts with a <c>#!</c> line.
        /// </summary>
        /// <param name="relativePath">The forward-slash path relative to the skill folder.</param>
        /// <param name="content">The content.</param>
        /// <returns>True when the file should be executable.</returns>
        public static bool ShouldBeExecutable(string relativePath, byte[] content)
        {
            string path = (relativePath ?? string.Empty).Replace('\\', '/');
            if (path.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase) || path.Contains("/scripts/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return content != null && content.Length >= 2 && content[0] == (byte)'#' && content[1] == (byte)'!';
        }

        /// <summary>
        /// Adds the executable bits (user, group, other) on Unix. Does nothing on Windows or when the file system
        /// refuses.
        /// </summary>
        /// <param name="path">The file.</param>
        public static void MakeExecutable(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                UnixFileMode mode = File.GetUnixFileMode(path);
                File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch (Exception)
            {
                // Best effort: a skill still works when run through its interpreter.
            }
        }

        /// <summary>
        /// A stable hash of a skill's <c>SKILL.md</c> text, used to tell whether a user edited an installed skill.
        /// </summary>
        /// <param name="skillMarkdown">The text.</param>
        /// <returns>The lowercase hex SHA-256 of the text with line endings normalized.</returns>
        public static string HashMarkdown(string skillMarkdown)
        {
            string normalized = (skillMarkdown ?? string.Empty).Replace("\r\n", "\n");
            byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        #endregion
    }
}
