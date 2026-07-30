namespace Mux.Core.Agent
{
    using System;
    using System.IO;

    /// <summary>
    /// Provides path resolution and safety checks relative to the agent's working directory.
    /// </summary>
    public static class WorkingDirectoryGuard
    {
        #region Public-Methods

        /// <summary>
        /// Resolves a requested path relative to the working directory. Callers that need to enforce or
        /// surface a working-directory boundary should check <see cref="IsWithinWorkingDirectory"/> on
        /// the result; this method itself performs no console output so <c>Mux.Core</c> stays
        /// presentation-free.
        /// </summary>
        /// <param name="requestedPath">The path to resolve.</param>
        /// <param name="workingDirectory">The current working directory.</param>
        /// <returns>The fully resolved path.</returns>
        public static string ResolveSafely(string requestedPath, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(requestedPath))
                throw new ArgumentException("Requested path cannot be null or empty.", nameof(requestedPath));
            if (string.IsNullOrWhiteSpace(workingDirectory))
                throw new ArgumentException("Working directory cannot be null or empty.", nameof(workingDirectory));

            if (Path.IsPathRooted(requestedPath))
            {
                return Path.GetFullPath(requestedPath);
            }

            return Path.GetFullPath(Path.Combine(workingDirectory, requestedPath));
        }

        /// <summary>
        /// Determines whether the resolved path is within the specified working directory.
        /// </summary>
        /// <param name="resolvedPath">The fully resolved path to check.</param>
        /// <param name="workingDirectory">The working directory to check against.</param>
        /// <returns>True if the resolved path starts with the working directory; otherwise false.</returns>
        public static bool IsWithinWorkingDirectory(string resolvedPath, string workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(resolvedPath) || string.IsNullOrWhiteSpace(workingDirectory))
                return false;

            string normalizedPath = Path.GetFullPath(resolvedPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedWorkDir = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return normalizedPath.StartsWith(normalizedWorkDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedPath, normalizedWorkDir, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
