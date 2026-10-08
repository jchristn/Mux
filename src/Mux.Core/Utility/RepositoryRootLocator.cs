namespace Mux.Core.Utility
{
    using System;
    using System.IO;

    /// <summary>
    /// Finds the root of the repository that contains a directory: the nearest ancestor (or the directory
    /// itself) holding a <c>.git</c> entry. A <c>.git</c> file counts too, so git worktrees and submodules
    /// resolve to their own root. Pure filesystem probing; no git process is started. Thread-safe.
    /// </summary>
    public static class RepositoryRootLocator
    {
        #region Public-Methods

        /// <summary>
        /// Returns the repository root for a directory, or null when the directory is not inside a repository.
        /// </summary>
        /// <param name="startDirectory">The directory to start from. Null, empty, or unresolvable returns null.</param>
        /// <returns>The absolute repository root without a trailing separator, or null.</returns>
        public static string? FindRepositoryRoot(string? startDirectory)
        {
            string? current = TryGetFullPath(startDirectory);
            while (!string.IsNullOrEmpty(current))
            {
                string marker = Path.Combine(current, ".git");
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return Path.TrimEndingDirectorySeparator(current);
                }

                DirectoryInfo? parent = Directory.GetParent(current);
                current = parent?.FullName;
            }

            return null;
        }

        /// <summary>
        /// Returns the project root for a working directory: its repository root when it is inside one,
        /// otherwise the working directory itself.
        /// </summary>
        /// <param name="workingDirectory">The working directory. Null or empty returns null.</param>
        /// <returns>The absolute project root, or null when the input is null, empty, or unresolvable.</returns>
        public static string? FindProjectRoot(string? workingDirectory)
        {
            string? full = TryGetFullPath(workingDirectory);
            if (full == null)
            {
                return null;
            }

            return FindRepositoryRoot(full) ?? Path.TrimEndingDirectorySeparator(full);
        }

        #endregion

        #region Private-Methods

        private static string? TryGetFullPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        #endregion
    }
}
