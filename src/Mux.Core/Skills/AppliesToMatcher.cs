namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Evaluates a skill's <c>appliesTo</c> globs against a project root. A glob is a relative path whose
    /// segments may use <c>*</c> and <c>?</c>, plus <c>**</c> for any number of directories (for example
    /// <c>package.json</c>, <c>requirements*.txt</c>, or <c>**/*.csproj</c>). Rooted globs and globs that
    /// climb out of the root with <c>..</c> never match. A <c>**</c> search skips dependency and build
    /// directories (such as <c>node_modules</c>, <c>.git</c>, <c>bin</c>, and <c>obj</c>) and stops after
    /// <see cref="MaxDirectoryVisits"/> directories so one broad glob cannot stall prompt assembly. Stateless
    /// apart from its configuration and safe to share across threads.
    /// </summary>
    public sealed class AppliesToMatcher
    {
        #region Private-Members

        private static readonly HashSet<string> _SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", "target", "dist", "build", "out", ".venv", "venv", "__pycache__", ".gradle", ".idea", ".vs"
        };

        private static readonly EnumerationOptions _EnumerationOptions = new EnumerationOptions
        {
            MatchType = MatchType.Simple,
            MatchCasing = MatchCasing.PlatformDefault,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.System
        };

        private int _MaxDepth = 8;
        private int _MaxDirectoryVisits = 2000;

        #endregion

        #region Public-Members

        /// <summary>
        /// The deepest directory level a <c>**</c> segment descends to. Default 8, minimum 1, maximum 64.
        /// </summary>
        public int MaxDepth
        {
            get => _MaxDepth;
            set => _MaxDepth = Math.Clamp(value, 1, 64);
        }

        /// <summary>
        /// The most directories one glob evaluation may visit. Default 2000, minimum 10, maximum 1000000.
        /// When the budget runs out the glob is treated as not matching.
        /// </summary>
        public int MaxDirectoryVisits
        {
            get => _MaxDirectoryVisits;
            set => _MaxDirectoryVisits = Math.Clamp(value, 10, 1000000);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns whether any of the globs matches an entry under the root. An empty glob list matches.
        /// </summary>
        /// <param name="root">The project root. Null, empty, or missing never matches a non-empty list.</param>
        /// <param name="globs">The globs. Null or empty matches.</param>
        /// <returns><c>true</c> when the list is empty or any glob matches.</returns>
        public bool AnyMatch(string? root, IReadOnlyList<string>? globs)
        {
            if (globs == null || globs.Count == 0)
            {
                return true;
            }

            foreach (string glob in globs)
            {
                if (Matches(root, glob))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether one glob matches a file or directory under the root.
        /// </summary>
        /// <param name="root">The project root. Null, empty, or missing returns false.</param>
        /// <param name="glob">The glob. Null, empty, rooted, or escaping returns false.</param>
        /// <returns><c>true</c> when the glob matches.</returns>
        public bool Matches(string? root, string? glob)
        {
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(glob) || !Directory.Exists(root))
            {
                return false;
            }

            string normalized = glob.Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(2);
            }

            normalized = normalized.Trim('/');
            if (normalized.Length == 0 || Path.IsPathRooted(glob.Trim()))
            {
                return false;
            }

            string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            foreach (string segment in segments)
            {
                if (segment == "..")
                {
                    return false;
                }
            }

            int budget = _MaxDirectoryVisits;
            return MatchFrom(root, segments, 0, 0, ref budget);
        }

        #endregion

        #region Private-Methods

        private bool MatchFrom(string directory, string[] segments, int index, int depth, ref int budget)
        {
            if (budget-- <= 0)
            {
                return false;
            }

            string segment = segments[index];
            bool last = index == segments.Length - 1;

            if (segment == "**")
            {
                if (last)
                {
                    return true;
                }

                if (MatchFrom(directory, segments, index + 1, depth, ref budget))
                {
                    return true;
                }

                if (depth >= _MaxDepth)
                {
                    return false;
                }

                foreach (string child in SafeEnumerateDirectories(directory, "*"))
                {
                    if (_SkippedDirectories.Contains(Path.GetFileName(child)))
                    {
                        continue;
                    }

                    if (MatchFrom(child, segments, index, depth + 1, ref budget))
                    {
                        return true;
                    }

                    if (budget <= 0)
                    {
                        return false;
                    }
                }

                return false;
            }

            bool wildcard = segment.IndexOfAny(new[] { '*', '?' }) >= 0;
            if (last)
            {
                if (!wildcard)
                {
                    string path = Path.Combine(directory, segment);
                    return File.Exists(path) || Directory.Exists(path);
                }

                foreach (string unused in SafeEnumerateEntries(directory, segment))
                {
                    return true;
                }

                return false;
            }

            if (!wildcard)
            {
                string next = Path.Combine(directory, segment);
                return Directory.Exists(next) && MatchFrom(next, segments, index + 1, depth + 1, ref budget);
            }

            foreach (string child in SafeEnumerateDirectories(directory, segment))
            {
                if (MatchFrom(child, segments, index + 1, depth + 1, ref budget))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string directory, string pattern)
        {
            try
            {
                return Directory.EnumerateDirectories(directory, pattern, _EnumerationOptions);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return Array.Empty<string>();
            }
        }

        private static IEnumerable<string> SafeEnumerateEntries(string directory, string pattern)
        {
            try
            {
                return Directory.EnumerateFileSystemEntries(directory, pattern, _EnumerationOptions);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                return Array.Empty<string>();
            }
        }

        #endregion
    }
}
