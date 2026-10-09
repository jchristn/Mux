namespace Mux.Core.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using Mux.Core.Utility;

    /// <summary>
    /// Persistent memory: facts the user or the model asked mux to keep across sessions. Each memory is one Markdown
    /// file with a small frontmatter (name, description, scope, created and updated times) under
    /// <c>&lt;root&gt;/&lt;project-key&gt;/</c> for project memories or <c>&lt;root&gt;/global/</c> for user-wide ones, and
    /// each folder has a <c>MEMORY.md</c> index that is rewritten on every change. Saving a memory whose name slugs
    /// to an existing one updates it. Files without a valid frontmatter are ignored. Writes are atomic
    /// (temporary file, then move). Thread-safe within a process.
    /// </summary>
    public sealed class MemoryStore
    {
        #region Private-Members

        private const string IndexFileName = "MEMORY.md";
        private const int MaxSlugLength = 60;
        private const int MaxDescriptionLength = 200;
        private const int MaxContentLength = 20000;

        private readonly object _Sync = new object();
        private readonly string _Root;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryStore"/> class.
        /// </summary>
        /// <param name="rootDirectory">The memory root (normally <c>&lt;config&gt;/memory</c>). Created on first write.</param>
        /// <exception cref="ArgumentException">Thrown when the root is blank.</exception>
        public MemoryStore(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("A memory directory is required.", nameof(rootDirectory));
            _Root = Path.GetFullPath(rootDirectory);
        }

        /// <summary>
        /// Creates the store under the active configuration directory (<c>&lt;config&gt;/memory</c>, honoring
        /// <c>MUX_CONFIG_DIR</c> and <c>--config-dir</c>).
        /// </summary>
        /// <returns>The store.</returns>
        public static MemoryStore FromConfigDirectory()
        {
            return new MemoryStore(Path.Combine(Mux.Core.Settings.SettingsLoader.GetConfigDirectory(), "memory"));
        }

        #endregion

        #region Public-Members

        /// <summary>The name of the global scope's folder.</summary>
        public const string GlobalFolderName = "global";

        /// <summary>The memory root directory.</summary>
        public string RootDirectory => _Root;

        /// <summary>
        /// Raised after a memory is saved or deleted.
        /// </summary>
        public event EventHandler? Changed;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Converts a name to its file slug: lowercase letters and digits joined by single hyphens, at most 60
        /// characters.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>The slug, or empty when the name has no letters or digits.</returns>
        public static string Slugify(string? name)
        {
            StringBuilder builder = new StringBuilder();
            bool hyphen = false;
            foreach (char c in (name ?? string.Empty).Trim().ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    if (hyphen && builder.Length > 0) builder.Append('-');
                    builder.Append(c);
                    hyphen = false;
                }
                else
                {
                    hyphen = true;
                }

                if (builder.Length >= MaxSlugLength) break;
            }

            return builder.ToString().Trim('-');
        }

        /// <summary>
        /// Returns the folder key for a working directory's project: the repository root's folder name plus a short
        /// hash of its full path, so two checkouts with the same name do not share memories.
        /// </summary>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The project key.</returns>
        public static string ProjectKey(string? workingDirectory)
        {
            string directory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory!;
            string root = RepositoryRootLocator.FindProjectRoot(directory) ?? directory;
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalized = OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            string leaf = Slugify(Path.GetFileName(full));
            if (leaf.Length == 0) leaf = "project";
            if (leaf.Length > 40) leaf = leaf.Substring(0, 40).Trim('-');
            return leaf + "-" + Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        }

        /// <summary>
        /// Returns the folder holding a scope's memories for a working directory.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="workingDirectory">The working directory (ignored for the global scope).</param>
        /// <returns>The folder path (it may not exist yet).</returns>
        public string DirectoryFor(MemoryScopeEnum scope, string? workingDirectory)
        {
            return Path.Combine(_Root, scope == MemoryScopeEnum.Global ? GlobalFolderName : ProjectKey(workingDirectory));
        }

        /// <summary>
        /// Lists a scope's memories, newest first.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The memories.</returns>
        public List<MemoryEntry> List(MemoryScopeEnum scope, string? workingDirectory)
        {
            List<MemoryEntry> entries = new List<MemoryEntry>();
            string directory = DirectoryFor(scope, workingDirectory);
            lock (_Sync)
            {
                if (!Directory.Exists(directory))
                {
                    return entries;
                }

                foreach (string file in Directory.EnumerateFiles(directory, "*.md"))
                {
                    if (string.Equals(Path.GetFileName(file), IndexFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    MemoryEntry? entry = TryRead(file, scope);
                    if (entry != null)
                    {
                        entries.Add(entry);
                    }
                }
            }

            entries.Sort((MemoryEntry a, MemoryEntry b) =>
            {
                int byTime = b.UpdatedUtc.CompareTo(a.UpdatedUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(a.Slug, b.Slug);
            });
            return entries;
        }

        /// <summary>
        /// Lists the project's memories followed by the global ones, each newest first.
        /// </summary>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The memories.</returns>
        public List<MemoryEntry> ListAll(string? workingDirectory)
        {
            List<MemoryEntry> all = List(MemoryScopeEnum.Project, workingDirectory);
            all.AddRange(List(MemoryScopeEnum.Global, workingDirectory));
            return all;
        }

        /// <summary>
        /// Finds a memory by name (or slug). Without a scope, the project is searched before the global scope.
        /// </summary>
        /// <param name="name">The name or slug.</param>
        /// <param name="scope">The scope, or null for project then global.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The memory, or null.</returns>
        public MemoryEntry? Get(string? name, MemoryScopeEnum? scope, string? workingDirectory)
        {
            string slug = Slugify(name);
            if (slug.Length == 0)
            {
                return null;
            }

            foreach (MemoryScopeEnum candidate in Scopes(scope))
            {
                string file = Path.Combine(DirectoryFor(candidate, workingDirectory), slug + ".md");
                lock (_Sync)
                {
                    if (File.Exists(file))
                    {
                        MemoryEntry? entry = TryRead(file, candidate);
                        if (entry != null) return entry;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Saves a memory, creating it or updating the one whose name has the same slug.
        /// </summary>
        /// <param name="name">The name. Must contain a letter or digit.</param>
        /// <param name="description">A one-line summary; the first line of the content is used when blank.</param>
        /// <param name="content">The full text; the description is used when blank.</param>
        /// <param name="scope">The scope.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <param name="created">True when a new memory was created, false when an existing one was updated.</param>
        /// <returns>The saved memory.</returns>
        /// <exception cref="ArgumentException">Thrown when the name has no letters or digits, or both description and content are blank.</exception>
        public MemoryEntry Save(string name, string? description, string? content, MemoryScopeEnum scope, string? workingDirectory, out bool created)
        {
            string slug = Slugify(name);
            if (slug.Length == 0) throw new ArgumentException("A memory name needs at least one letter or digit.", nameof(name));
            string body = (content ?? string.Empty).Trim();
            string summary = OneLine(description);
            if (summary.Length == 0) summary = OneLine(FirstLine(body));
            if (summary.Length == 0) throw new ArgumentException("A memory needs a description or content.", nameof(description));
            if (body.Length == 0) body = summary;
            if (body.Length > MaxContentLength) body = body.Substring(0, MaxContentLength);
            if (summary.Length > MaxDescriptionLength) summary = summary.Substring(0, MaxDescriptionLength - 3).TrimEnd() + "...";

            string directory = DirectoryFor(scope, workingDirectory);
            string file = Path.Combine(directory, slug + ".md");
            MemoryEntry entry;
            lock (_Sync)
            {
                Directory.CreateDirectory(directory);
                MemoryEntry? existing = File.Exists(file) ? TryRead(file, scope) : null;
                DateTime now = DateTime.UtcNow;
                entry = new MemoryEntry
                {
                    Name = OneLine(name),
                    Slug = slug,
                    Description = summary,
                    Content = body,
                    Scope = scope,
                    CreatedUtc = existing?.CreatedUtc ?? now,
                    UpdatedUtc = now,
                    FilePath = file
                };
                created = existing == null;
                WriteAtomic(file, Serialize(entry));
                WriteIndexNoLock(directory, scope);
            }

            OnChanged();
            return entry;
        }

        /// <summary>
        /// Deletes a memory by name. Without a scope, the project is tried before the global scope.
        /// </summary>
        /// <param name="name">The name or slug.</param>
        /// <param name="scope">The scope, or null.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The deleted memory, or null when none matched.</returns>
        public MemoryEntry? Delete(string? name, MemoryScopeEnum? scope, string? workingDirectory)
        {
            MemoryEntry? entry = Get(name, scope, workingDirectory);
            if (entry == null)
            {
                return null;
            }

            lock (_Sync)
            {
                File.Delete(entry.FilePath);
                WriteIndexNoLock(Path.GetDirectoryName(entry.FilePath)!, entry.Scope);
            }

            OnChanged();
            return entry;
        }

        /// <summary>
        /// Deletes every memory in a scope.
        /// </summary>
        /// <param name="scope">The scope.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The number deleted.</returns>
        public int Clear(MemoryScopeEnum scope, string? workingDirectory)
        {
            List<MemoryEntry> entries = List(scope, workingDirectory);
            if (entries.Count == 0)
            {
                return 0;
            }

            string directory = DirectoryFor(scope, workingDirectory);
            lock (_Sync)
            {
                foreach (MemoryEntry entry in entries)
                {
                    File.Delete(entry.FilePath);
                }

                WriteIndexNoLock(directory, scope);
            }

            OnChanged();
            return entries.Count;
        }

        /// <summary>
        /// Searches the project and global memories. Every whitespace-separated term must appear (case-insensitive)
        /// in the name, description, or content. A blank query returns everything.
        /// </summary>
        /// <param name="query">The search text.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <returns>The matches, project first, newest first within each scope.</returns>
        public List<MemoryEntry> Search(string? query, string? workingDirectory)
        {
            string[] terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            List<MemoryEntry> matches = new List<MemoryEntry>();
            foreach (MemoryEntry entry in ListAll(workingDirectory))
            {
                string haystack = entry.Name + "\n" + entry.Description + "\n" + entry.Content;
                bool all = true;
                foreach (string term in terms)
                {
                    if (haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        all = false;
                        break;
                    }
                }

                if (all) matches.Add(entry);
            }

            return matches;
        }

        /// <summary>
        /// Parses a scope name (<c>project</c> or <c>global</c>, case-insensitive).
        /// </summary>
        /// <param name="value">The text.</param>
        /// <param name="scope">The parsed scope.</param>
        /// <returns>True when recognized.</returns>
        public static bool TryParseScope(string? value, out MemoryScopeEnum scope)
        {
            scope = MemoryScopeEnum.Project;
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "project":
                case "repo":
                case "local":
                    scope = MemoryScopeEnum.Project;
                    return true;
                case "global":
                case "user":
                    scope = MemoryScopeEnum.Global;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Builds a memory name from free text (used by <c>#</c> quick-add): the first few words.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>A short name, or empty when the text has no letters or digits.</returns>
        public static string NameFromText(string? text)
        {
            string[] words = (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            List<string> kept = new List<string>();
            foreach (string word in words)
            {
                if (Slugify(word).Length == 0) continue;
                kept.Add(word);
                if (kept.Count == 6) break;
            }

            return Slugify(string.Join(" ", kept));
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<MemoryScopeEnum> Scopes(MemoryScopeEnum? scope)
        {
            if (scope.HasValue)
            {
                yield return scope.Value;
                yield break;
            }

            yield return MemoryScopeEnum.Project;
            yield return MemoryScopeEnum.Global;
        }

        private static MemoryEntry? TryRead(string file, MemoryScopeEnum scope)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception)
            {
                return null;
            }

            string normalized = text.Replace("\r\n", "\n");
            if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
            {
                return null;
            }

            int end = normalized.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end < 0)
            {
                return null;
            }

            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in normalized.Substring(4, end - 4).Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                fields[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
            }

            if (!fields.TryGetValue("name", out string? name) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            int bodyStart = normalized.IndexOf('\n', end + 4);
            string body = bodyStart < 0 ? string.Empty : normalized.Substring(bodyStart + 1).Trim();
            fields.TryGetValue("description", out string? description);
            return new MemoryEntry
            {
                Name = name,
                Slug = Path.GetFileNameWithoutExtension(file),
                Description = description ?? string.Empty,
                Content = body,
                Scope = scope,
                CreatedUtc = ParseTime(fields, "created", file),
                UpdatedUtc = ParseTime(fields, "updated", file),
                FilePath = file
            };
        }

        private static DateTime ParseTime(Dictionary<string, string> fields, string key, string file)
        {
            if (fields.TryGetValue(key, out string? value)
                && DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return parsed;
            }

            try
            {
                return File.GetLastWriteTimeUtc(file);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        private static string Serialize(MemoryEntry entry)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("---\n");
            builder.Append("name: ").Append(entry.Name).Append('\n');
            builder.Append("description: ").Append(entry.Description).Append('\n');
            builder.Append("scope: ").Append(entry.Scope == MemoryScopeEnum.Global ? "global" : "project").Append('\n');
            builder.Append("created: ").Append(entry.CreatedUtc.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("updated: ").Append(entry.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("---\n\n");
            builder.Append(entry.Content).Append('\n');
            return builder.ToString();
        }

        private void WriteIndexNoLock(string directory, MemoryScopeEnum scope)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            List<MemoryEntry> entries = new List<MemoryEntry>();
            foreach (string file in Directory.EnumerateFiles(directory, "*.md"))
            {
                if (string.Equals(Path.GetFileName(file), IndexFileName, StringComparison.OrdinalIgnoreCase)) continue;
                MemoryEntry? entry = TryRead(file, scope);
                if (entry != null) entries.Add(entry);
            }

            entries.Sort((MemoryEntry a, MemoryEntry b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            StringBuilder builder = new StringBuilder();
            builder.Append("# Memory index (").Append(scope == MemoryScopeEnum.Global ? "global" : "project").Append(")\n\n");
            foreach (MemoryEntry entry in entries)
            {
                builder.Append("- [").Append(entry.Name).Append("](").Append(entry.Slug).Append(".md): ").Append(entry.Description).Append('\n');
            }

            WriteAtomic(Path.Combine(directory, IndexFileName), builder.ToString());
        }

        private static void WriteAtomic(string path, string content)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
        }

        private static string OneLine(string? text)
        {
            return (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string FirstLine(string text)
        {
            int newline = text.IndexOf('\n');
            return newline < 0 ? text : text.Substring(0, newline);
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
