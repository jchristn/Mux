namespace Mux.Core.Utility
{
    using System;
    using System.IO;

    /// <summary>
    /// The outcome of resolving a user-supplied working-directory path: either a resolved absolute directory
    /// or a human-readable error.
    /// </summary>
    public sealed class WorkingDirectoryResolution
    {
        private WorkingDirectoryResolution(bool ok, string path, string error)
        {
            Ok = ok;
            Path = path;
            Error = error;
        }

        /// <summary>Whether resolution succeeded.</summary>
        public bool Ok { get; }

        /// <summary>The resolved absolute directory path when <see cref="Ok"/> is true; otherwise empty.</summary>
        public string Path { get; }

        /// <summary>The error message when <see cref="Ok"/> is false; otherwise empty.</summary>
        public string Error { get; }

        /// <summary>Creates a successful resolution.</summary>
        /// <param name="path">The resolved absolute path.</param>
        /// <returns>The resolution.</returns>
        public static WorkingDirectoryResolution Success(string path) => new WorkingDirectoryResolution(true, path ?? string.Empty, string.Empty);

        /// <summary>Creates a failed resolution.</summary>
        /// <param name="error">The error message.</param>
        /// <returns>The resolution.</returns>
        public static WorkingDirectoryResolution Failure(string error) => new WorkingDirectoryResolution(false, string.Empty, error ?? string.Empty);
    }

    /// <summary>
    /// Resolves a user-supplied path (as typed after <c>/cwd</c>) into an absolute working directory. Shared by
    /// the TUI and desktop so the two surfaces accept the same syntax: surrounding quotes are stripped, a
    /// leading <c>~</c> expands to the user profile, relative paths resolve against the current working
    /// directory, and trailing separators are trimmed. The existence check is injectable so the pure
    /// resolution logic is unit-testable without touching the file system.
    /// </summary>
    public static class WorkingDirectoryResolver
    {
        /// <summary>
        /// Normalizes a raw path input to an absolute path without checking that it exists. Strips matching
        /// surrounding quotes, expands a leading <c>~</c>, resolves relative paths against
        /// <paramref name="currentDirectory"/>, and trims trailing separators (root paths keep their slash).
        /// </summary>
        /// <param name="currentDirectory">The directory relative paths resolve against.</param>
        /// <param name="input">The raw path text.</param>
        /// <returns>The normalized absolute path, or an empty string when the input is blank.</returns>
        public static string Normalize(string currentDirectory, string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string trimmed = input.Trim();

            // Strip a single pair of matching surrounding quotes (users paste quoted paths with spaces).
            if (trimmed.Length >= 2)
            {
                char first = trimmed[0];
                char last = trimmed[trimmed.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
                }
            }

            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            // Expand a leading ~ to the user's home directory.
            if (trimmed == "~")
            {
                trimmed = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }
            else if (trimmed.StartsWith("~/", StringComparison.Ordinal) || trimmed.StartsWith("~\\", StringComparison.Ordinal))
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                trimmed = Path.Combine(home, trimmed.Substring(2));
            }

            string basePath = string.IsNullOrWhiteSpace(currentDirectory) ? Directory.GetCurrentDirectory() : currentDirectory;

            string full;
            try
            {
                full = Path.GetFullPath(trimmed, basePath);
            }
            catch (Exception)
            {
                // GetFullPath throws on invalid characters; fall back to the trimmed input so the caller's
                // existence check reports a clean "no such directory" rather than surfacing a raw exception.
                return trimmed;
            }

            return TrimTrailingSeparators(full);
        }

        /// <summary>
        /// Resolves a raw path input to an existing absolute directory.
        /// </summary>
        /// <param name="currentDirectory">The directory relative paths resolve against.</param>
        /// <param name="input">The raw path text (as typed after <c>/cwd</c>).</param>
        /// <param name="directoryExists">Predicate that reports whether a directory exists. Defaults to <see cref="Directory.Exists(string)"/>.</param>
        /// <returns>A success resolution with the absolute path, or a failure with a message.</returns>
        public static WorkingDirectoryResolution Resolve(string currentDirectory, string? input, Func<string, bool>? directoryExists = null)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return WorkingDirectoryResolution.Failure("A directory path is required.");
            }

            string normalized = Normalize(currentDirectory, input);
            if (string.IsNullOrEmpty(normalized))
            {
                return WorkingDirectoryResolution.Failure("A directory path is required.");
            }

            Func<string, bool> exists = directoryExists ?? Directory.Exists;
            if (!exists(normalized))
            {
                return WorkingDirectoryResolution.Failure("No such directory: " + normalized);
            }

            return WorkingDirectoryResolution.Success(normalized);
        }

        private static string TrimTrailingSeparators(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            // Keep a root path intact (e.g. "C:\" or "/").
            string root = Path.GetPathRoot(path) ?? string.Empty;
            string trimmed = path;
            while (trimmed.Length > root.Length
                && (trimmed[trimmed.Length - 1] == Path.DirectorySeparatorChar || trimmed[trimmed.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed.Length == 0 ? path : trimmed;
        }
    }
}
