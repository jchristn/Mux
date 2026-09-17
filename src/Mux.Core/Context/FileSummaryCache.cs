namespace Mux.Core.Context
{
    using System;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using Mux.Core.Settings;

    /// <summary>
    /// A loose-file cache of large-file summaries under <c>~/.mux/cache/file-summaries/</c>, one artifact per
    /// entry, content-addressed by <c>(path, contentHash, mode, model)</c>. Because the content hash is part
    /// of the key, editing a file misses automatically; a stale entry is never served. Loose files were chosen
    /// over a database on purpose: there is no schema to migrate, a corrupt entry costs one file, and eviction
    /// is a directory sweep. Every operation is best-effort — a cache failure never propagates to the caller.
    /// </summary>
    public sealed class FileSummaryCache
    {
        private readonly string _Directory;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSummaryCache"/> class.
        /// </summary>
        /// <param name="directory">The cache directory; null uses <c>&lt;config&gt;/cache/file-summaries</c>.</param>
        public FileSummaryCache(string? directory)
        {
            _Directory = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(SettingsLoader.GetConfigDirectory(), "cache", "file-summaries")
                : directory!;
        }

        /// <summary>
        /// Computes the content hash used in a cache key.
        /// </summary>
        /// <param name="content">The file content; null is treated as empty.</param>
        /// <returns>A lowercase hex SHA-256 of the content.</returns>
        public static string ComputeContentHash(string? content)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
            return ToHex(hash);
        }

        /// <summary>
        /// Builds the composite cache key from its parts.
        /// </summary>
        /// <param name="path">The file path.</param>
        /// <param name="contentHash">The content hash.</param>
        /// <param name="mode">The mode (for example <c>summarize</c>).</param>
        /// <param name="model">The summarizing model key.</param>
        /// <returns>The composite key string.</returns>
        public static string BuildKey(string? path, string? contentHash, string? mode, string? model)
        {
            return string.Join("", path ?? string.Empty, contentHash ?? string.Empty, mode ?? string.Empty, model ?? string.Empty);
        }

        /// <summary>
        /// Attempts to read a cached summary for a composite key.
        /// </summary>
        /// <param name="compositeKey">The composite key from <see cref="BuildKey"/>.</param>
        /// <param name="value">The cached text when found; otherwise empty.</param>
        /// <returns>True when a cache entry exists and was read; otherwise false.</returns>
        public bool TryGet(string compositeKey, out string value)
        {
            value = string.Empty;
            try
            {
                string file = PathFor(compositeKey);
                if (!File.Exists(file))
                {
                    return false;
                }

                value = File.ReadAllText(file);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Writes a summary for a composite key. Best-effort: failures are swallowed.
        /// </summary>
        /// <param name="compositeKey">The composite key from <see cref="BuildKey"/>.</param>
        /// <param name="value">The summary text to cache.</param>
        public void Put(string compositeKey, string value)
        {
            try
            {
                Directory.CreateDirectory(_Directory);
                string file = PathFor(compositeKey);
                string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, value ?? string.Empty);
                File.Move(temp, file, overwrite: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Deletes cache entries older than a retention window. Best-effort.
        /// </summary>
        /// <param name="retentionDays">The maximum age in days to keep (floored at 1).</param>
        /// <returns>The number of entries deleted.</returns>
        public int Evict(int retentionDays)
        {
            int days = Math.Max(1, retentionDays);
            int deleted = 0;
            try
            {
                if (!Directory.Exists(_Directory))
                {
                    return 0;
                }

                DateTime cutoff = DateTime.UtcNow.AddDays(-days);
                foreach (string file in Directory.GetFiles(_Directory, "*.txt"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                        {
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch (IOException)
                    {
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            return deleted;
        }

        private string PathFor(string compositeKey)
        {
            using SHA256 sha = SHA256.Create();
            string name = ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(compositeKey ?? string.Empty)));
            return Path.Combine(_Directory, name + ".txt");
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
