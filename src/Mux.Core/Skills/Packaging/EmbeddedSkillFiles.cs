namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Reads skill files embedded in <c>Mux.Core</c> (the <c>Skills/Bundled</c> and <c>Skills/Packs</c> folders) or
    /// the same layout from a folder on disk, as forward-slash relative paths mapped to bytes.
    /// </summary>
    public static class EmbeddedSkillFiles
    {
        #region Public-Members

        /// <summary>The logical-name prefix of embedded bundled default skills.</summary>
        public const string BundledPrefix = "Mux.Core.Skills.Bundled/";

        /// <summary>The logical-name prefix of embedded skill packs.</summary>
        public const string PacksPrefix = "Mux.Core.Skills.Packs/";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reads every embedded resource whose logical name starts with <paramref name="prefix"/>.
        /// </summary>
        /// <param name="prefix">The prefix (for example <see cref="BundledPrefix"/>).</param>
        /// <returns>The files by path relative to the prefix, with forward slashes.</returns>
        public static Dictionary<string, byte[]> ReadEmbedded(string prefix)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            Assembly assembly = typeof(EmbeddedSkillFiles).Assembly;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                string normalized = name.Replace('\\', '/');
                if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                using (Stream? stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        continue;
                    }

                    using (MemoryStream buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        files[normalized.Substring(prefix.Length)] = buffer.ToArray();
                    }
                }
            }

            return files;
        }

        /// <summary>
        /// Reads every file under <paramref name="directory"/> (skipping dot-folders and dot-files).
        /// </summary>
        /// <param name="directory">The folder.</param>
        /// <returns>The files by forward-slash path relative to the folder; empty when the folder is missing.</returns>
        public static Dictionary<string, byte[]> ReadDirectory(string directory)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return files;
            }

            string root = Path.GetFullPath(directory);
            foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                bool hidden = false;
                foreach (string segment in relative.Split('/'))
                {
                    if (segment.StartsWith(".", StringComparison.Ordinal))
                    {
                        hidden = true;
                        break;
                    }
                }

                if (!hidden)
                {
                    files[relative] = File.ReadAllBytes(file);
                }
            }

            return files;
        }

        /// <summary>
        /// Groups files by their first path segment: <c>a/b/c</c> goes to group <c>a</c> as <c>b/c</c>. Files at the
        /// top level (no folder) are ignored.
        /// </summary>
        /// <param name="files">The files.</param>
        /// <returns>The groups, sorted by name.</returns>
        public static SortedDictionary<string, Dictionary<string, byte[]>> GroupByFolder(IReadOnlyDictionary<string, byte[]> files)
        {
            SortedDictionary<string, Dictionary<string, byte[]>> groups = new SortedDictionary<string, Dictionary<string, byte[]>>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                int slash = file.Key.IndexOf('/');
                if (slash <= 0)
                {
                    continue;
                }

                string folder = file.Key.Substring(0, slash);
                if (!groups.TryGetValue(folder, out Dictionary<string, byte[]>? group))
                {
                    group = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                    groups[folder] = group;
                }

                group[file.Key.Substring(slash + 1)] = file.Value;
            }

            return groups;
        }

        #endregion
    }
}
