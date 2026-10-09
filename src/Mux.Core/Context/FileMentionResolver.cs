namespace Mux.Core.Context
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Finds <c>@path</c> mentions in a prompt, resolves them against a working directory, and attaches the files
    /// (or directory listings) to the prompt in a delimited <c>&lt;mentioned-files&gt;</c> block. Large files go
    /// through <see cref="FileContextBuilder"/> and arrive as structural maps with line ranges rather than being cut
    /// off. Also ranks project paths for <c>@</c> completion in composers. Shared by every surface so a mention
    /// means the same thing in the terminal, <c>mux print</c>, the desktop app, the web dashboard, and VS Code.
    /// </summary>
    /// <remarks>
    /// Mentions start at the beginning of the text or after whitespace or an opening bracket or quote, so
    /// <c>user@example.com</c> is not a mention, and <c>@@</c> escapes a literal <c>@</c>. Paths outside the working
    /// directory are refused. The attached text is capped by <see cref="MaxBytes"/> (setting
    /// <c>fileMentionMaxBytes</c>).
    /// </remarks>
    public sealed class FileMentionResolver
    {
        #region Private-Members

        private const string MentionStarters = "([{<\"'`,;:";
        private const string TrailingPunctuation = ".,;:!?)]}>'\"`";
        private const int MaxListingEntries = 200;
        private const int MaxIndexedEntries = 20000;
        private const int MaxIndexDepth = 12;

        private static readonly HashSet<string> _SkippedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".hg", ".svn", "node_modules", "bin", "obj", "target", "dist", "build", "out", ".venv", "venv", "__pycache__",
            ".gradle", ".idea", ".vs", ".vscode-test", ".next", ".nuxt", ".cache", ".turbo", "coverage", ".pytest_cache", ".mypy_cache", ".tox"
        };

        private static readonly bool _CaseInsensitivePaths = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        private readonly string _Root;
        private readonly object _IndexSync = new object();
        private List<string>? _Index;
        private DateTime _IndexBuiltUtc;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="FileMentionResolver"/> class.
        /// </summary>
        /// <param name="workingDirectory">The directory mentions are resolved against. Must not be blank.</param>
        /// <param name="maxBytes">The most attached text, in UTF-8 bytes; 0 turns attachments off.</param>
        /// <param name="inlineThresholdBytes">Files larger than this are attached as a structural map instead of in full.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="workingDirectory"/> is blank.</exception>
        public FileMentionResolver(string workingDirectory, int maxBytes = DefaultMaxBytes, int inlineThresholdBytes = DefaultInlineThresholdBytes)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory)) throw new ArgumentException("A working directory is required.", nameof(workingDirectory));
            _Root = Path.GetFullPath(workingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (_Root.Length == 0)
            {
                _Root = Path.GetPathRoot(Path.GetFullPath(workingDirectory)) ?? workingDirectory;
            }

            MaxBytes = Math.Max(0, maxBytes);
            InlineThresholdBytes = Math.Max(1, inlineThresholdBytes);
        }

        #endregion

        #region Public-Members

        /// <summary>The default attachment budget (256 KB).</summary>
        public const int DefaultMaxBytes = 262144;

        /// <summary>The default size above which a file is attached as a structural map (64 KB).</summary>
        public const int DefaultInlineThresholdBytes = 65536;

        /// <summary>The largest allowed attachment budget.</summary>
        public const int MaxBytesLimit = 16777216;

        /// <summary>The working directory mentions are resolved against.</summary>
        public string WorkingDirectory => _Root;

        /// <summary>The most attached text, in UTF-8 bytes; 0 turns attachments off.</summary>
        public int MaxBytes { get; }

        /// <summary>Files larger than this many bytes are attached as a structural map.</summary>
        public int InlineThresholdBytes { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Finds every <c>@path</c> mention in a prompt.
        /// </summary>
        /// <param name="text">The prompt.</param>
        /// <returns>The mentions in order (duplicates included).</returns>
        public static List<FileMention> Parse(string? text)
        {
            List<FileMention> mentions = new List<FileMention>();
            string value = text ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] != '@')
                {
                    continue;
                }

                if (i + 1 < value.Length && value[i + 1] == '@')
                {
                    i++;
                    continue;
                }

                if (i > 0 && !char.IsWhiteSpace(value[i - 1]) && MentionStarters.IndexOf(value[i - 1]) < 0)
                {
                    continue;
                }

                if (i + 1 >= value.Length || char.IsWhiteSpace(value[i + 1]))
                {
                    continue;
                }

                if (value[i + 1] == '"')
                {
                    int close = value.IndexOf('"', i + 2);
                    if (close < 0)
                    {
                        continue;
                    }

                    string quoted = value.Substring(i + 2, close - i - 2);
                    if (quoted.Trim().Length > 0)
                    {
                        mentions.Add(new FileMention
                        {
                            Raw = value.Substring(i, close - i + 1),
                            Path = quoted.Trim(),
                            Start = i,
                            Length = close - i + 1,
                            Quoted = true,
                            IsDirectoryHint = EndsWithSeparator(quoted.Trim())
                        });
                    }

                    i = close;
                    continue;
                }

                int end = i + 1;
                while (end < value.Length && !char.IsWhiteSpace(value[end]))
                {
                    end++;
                }

                string token = value.Substring(i + 1, end - i - 1);
                while (token.Length > 0 && TrailingPunctuation.IndexOf(token[token.Length - 1]) >= 0)
                {
                    token = token.Substring(0, token.Length - 1);
                }

                if (token.Length > 0 && token != "/" && token != "\\")
                {
                    mentions.Add(new FileMention
                    {
                        Raw = "@" + token,
                        Path = token,
                        Start = i,
                        Length = token.Length + 1,
                        Quoted = false,
                        IsDirectoryHint = EndsWithSeparator(token)
                    });
                }

                i = end - 1;
            }

            return mentions;
        }

        /// <summary>
        /// Resolves the mentions in a prompt and attaches what they name.
        /// </summary>
        /// <param name="prompt">The prompt.</param>
        /// <param name="cancellationToken">Cancels the file reads.</param>
        /// <returns>The prompt to send and what was attached.</returns>
        public async Task<FileMentionResult> ResolveAsync(string? prompt, CancellationToken cancellationToken)
        {
            string original = prompt ?? string.Empty;
            FileMentionResult result = new FileMentionResult { OriginalPrompt = original, Prompt = original };
            List<FileMention> mentions = Parse(original);
            if (mentions.Count == 0)
            {
                return result;
            }

            result.HadMentions = true;
            if (MaxBytes <= 0)
            {
                result.Notes.Add("File mentions are turned off (fileMentionMaxBytes is 0); the @ paths were sent as typed.");
                return result;
            }

            HashSet<string> seen = new HashSet<string>(_CaseInsensitivePaths ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            int remaining = MaxBytes;
            foreach (FileMention mention in mentions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryResolvePath(mention.Path, out string fullPath, out string error))
                {
                    result.Unresolved.Add(mention.Raw + ": " + error);
                    continue;
                }

                if (!seen.Add(fullPath))
                {
                    continue;
                }

                FileMentionAttachment? attachment;
                if (Directory.Exists(fullPath))
                {
                    attachment = BuildListing(mention, fullPath);
                }
                else if (File.Exists(fullPath))
                {
                    if (mention.IsDirectoryHint)
                    {
                        result.Unresolved.Add(mention.Raw + ": is a file, not a directory");
                        continue;
                    }

                    attachment = await BuildFileAsync(mention, fullPath, InlineThresholdBytes, cancellationToken).ConfigureAwait(false);
                    if (attachment == null)
                    {
                        result.Unresolved.Add(mention.Raw + ": binary file, not attached");
                        continue;
                    }

                    if (attachment.Bytes > remaining && attachment.Inlined)
                    {
                        FileMentionAttachment? mapped = await BuildFileAsync(mention, fullPath, 1, cancellationToken).ConfigureAwait(false);
                        if (mapped != null && mapped.Bytes <= remaining)
                        {
                            attachment = mapped;
                        }
                    }
                }
                else
                {
                    result.Unresolved.Add(mention.Raw + ": not found");
                    continue;
                }

                if (attachment.Bytes > remaining)
                {
                    result.Notes.Add(mention.Raw + " was not attached: it needs " + attachment.Bytes.ToString(CultureInfo.InvariantCulture)
                        + " bytes and only " + remaining.ToString(CultureInfo.InvariantCulture) + " of the fileMentionMaxBytes budget ("
                        + MaxBytes.ToString(CultureInfo.InvariantCulture) + ") are left.");
                    continue;
                }

                remaining -= attachment.Bytes;
                result.TotalBytes += attachment.Bytes;
                result.Attachments.Add(attachment);
            }

            if (result.Attachments.Count > 0)
            {
                result.Block = BuildBlock(result.Attachments);
                result.Prompt = original.TrimEnd() + "\n\n" + result.Block;
            }

            return result;
        }

        /// <summary>
        /// Ranks project paths for <c>@</c> completion: exact path, then file names that start with the query, paths
        /// that start with it, names and paths that contain it, and finally fuzzy (in-order letters) matches. Shorter
        /// paths win ties. Dependency and build folders are never offered. Directories end with <c>/</c>.
        /// </summary>
        /// <param name="query">The text typed after <c>@</c>; empty lists top-level entries.</param>
        /// <param name="max">The most suggestions to return (1 to 100).</param>
        /// <returns>Relative paths with forward slashes.</returns>
        public List<string> Complete(string? query, int max = 10)
        {
            int limit = Math.Clamp(max, 1, 100);
            string needle = (query ?? string.Empty).Trim().Trim('"').Replace('\\', '/');
            while (needle.StartsWith("./", StringComparison.Ordinal))
            {
                needle = needle.Substring(2);
            }

            List<string> index = GetIndex();
            if (needle.Length == 0)
            {
                List<string> top = index.FindAll(p => p.IndexOf('/') < 0 || (p.EndsWith("/", StringComparison.Ordinal) && p.IndexOf('/') == p.Length - 1));
                top.Sort((a, b) => CompareTopLevel(a, b));
                return top.GetRange(0, Math.Min(limit, top.Count));
            }

            List<RankedPath> ranked = new List<RankedPath>();
            foreach (string path in index)
            {
                int rank = Rank(path, needle);
                if (rank >= 0)
                {
                    ranked.Add(new RankedPath(path, rank));
                }
            }

            ranked.Sort((a, b) =>
            {
                int byRank = a.Rank.CompareTo(b.Rank);
                if (byRank != 0) return byRank;
                int byLength = a.Path.Length.CompareTo(b.Path.Length);
                return byLength != 0 ? byLength : string.CompareOrdinal(a.Path, b.Path);
            });

            List<string> results = new List<string>();
            for (int i = 0; i < ranked.Count && results.Count < limit; i++)
            {
                results.Add(ranked[i].Path);
            }

            return results;
        }

        /// <summary>
        /// Finds the <c>@</c> token being typed at the end of <paramref name="textBeforeCaret"/>, for composer completion.
        /// </summary>
        /// <param name="textBeforeCaret">The composer text up to the caret.</param>
        /// <param name="tokenStart">The index of the <c>@</c>.</param>
        /// <param name="query">The text typed after the <c>@</c>.</param>
        /// <returns>True when the caret is inside a mention.</returns>
        public static bool TryGetActiveToken(string? textBeforeCaret, out int tokenStart, out string query)
        {
            tokenStart = -1;
            query = string.Empty;
            string text = textBeforeCaret ?? string.Empty;
            int start = text.Length;
            while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
            {
                start--;
            }

            string token = text.Substring(start);
            int at = token.IndexOf('@');
            if (at < 0)
            {
                return false;
            }

            if (at > 0 && MentionStarters.IndexOf(token[at - 1]) < 0)
            {
                return false;
            }

            if (token.Length > at + 1 && token[at + 1] == '@')
            {
                return false;
            }

            tokenStart = start + at;
            query = token.Substring(at + 1).TrimStart('"');
            return true;
        }

        /// <summary>
        /// Formats a completed path as a mention, quoting it when it contains whitespace.
        /// </summary>
        /// <param name="relativePath">The relative path.</param>
        /// <returns>The mention text, for example <c>@src/app.ts</c> or <c>@"My Docs/notes.md"</c>.</returns>
        public static string FormatMention(string relativePath)
        {
            string path = relativePath ?? string.Empty;
            foreach (char c in path)
            {
                if (char.IsWhiteSpace(c))
                {
                    return "@\"" + path + "\"";
                }
            }

            return "@" + path;
        }

        /// <summary>
        /// Writes a one-line summary of a resolution for a notice, or null when there were no mentions.
        /// </summary>
        /// <param name="result">The result.</param>
        /// <returns>Lines describing what was attached, left out, or capped.</returns>
        public static List<string> Describe(FileMentionResult result)
        {
            List<string> lines = new List<string>();
            if (result == null || !result.HadMentions)
            {
                return lines;
            }

            foreach (FileMentionAttachment attachment in result.Attachments)
            {
                lines.Add("Attached " + attachment.RelativePath + " (" + attachment.Summary + ")");
            }

            foreach (string unresolved in result.Unresolved)
            {
                lines.Add("Left as typed: " + unresolved);
            }

            lines.AddRange(result.Notes);
            return lines;
        }

        #endregion

        #region Private-Methods

        private bool TryResolvePath(string path, out string fullPath, out string error)
        {
            fullPath = string.Empty;
            error = string.Empty;
            string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            try
            {
                fullPath = Path.GetFullPath(Path.IsPathRooted(normalized) ? normalized : Path.Combine(_Root, normalized))
                    .TrimEnd(Path.DirectorySeparatorChar);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                error = "not a valid path";
                return false;
            }

            if (fullPath.Length == 0)
            {
                error = "not a valid path";
                return false;
            }

            StringComparison comparison = _CaseInsensitivePaths ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            bool inside = string.Equals(fullPath, _Root, comparison) || fullPath.StartsWith(_Root + Path.DirectorySeparatorChar, comparison);
            if (!inside)
            {
                error = "outside the working directory, not attached";
                return false;
            }

            return true;
        }

        private string Relative(string fullPath, bool directory)
        {
            string relative = Path.GetRelativePath(_Root, fullPath).Replace('\\', '/');
            if (relative == ".")
            {
                relative = "./";
            }
            else if (directory)
            {
                relative += "/";
            }

            return relative;
        }

        private async Task<FileMentionAttachment?> BuildFileAsync(FileMention mention, string fullPath, int inlineThreshold, CancellationToken cancellationToken)
        {
            byte[] bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            int probe = Math.Min(bytes.Length, 8192);
            for (int i = 0; i < probe; i++)
            {
                if (bytes[i] == 0)
                {
                    return null;
                }
            }

            string content = Encoding.UTF8.GetString(bytes);
            if (content.Length > 0 && content[0] == '﻿')
            {
                content = content.Substring(1);
            }

            string relative = Relative(fullPath, false);
            FileContextResult built = await new FileContextBuilder(null)
                .BuildAsync(new FileContextRequest(relative, content, FileContextMode.Map, inlineThreshold, 40, 200, null, string.Empty), null, cancellationToken)
                .ConfigureAwait(false);
            int lines = CountLines(content);
            return new FileMentionAttachment
            {
                Raw = mention.Raw,
                RelativePath = relative,
                FullPath = fullPath,
                Kind = FileMentionKindEnum.File,
                Text = built.Text,
                Bytes = Encoding.UTF8.GetByteCount(built.Text),
                Lines = lines,
                Inlined = built.Inlined,
                Summary = (built.Inlined ? "full, " : "structural map, ") + lines.ToString(CultureInfo.InvariantCulture) + (lines == 1 ? " line" : " lines")
            };
        }

        private FileMentionAttachment BuildListing(FileMention mention, string fullPath)
        {
            List<string> entries = new List<string>();
            int total = 0;
            try
            {
                List<string> directories = new List<string>(Directory.EnumerateDirectories(fullPath));
                directories.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string directory in directories)
                {
                    string name = Path.GetFileName(directory);
                    if (_SkippedDirectories.Contains(name)) continue;
                    total++;
                    if (entries.Count < MaxListingEntries) entries.Add(name + "/");
                }

                List<string> files = new List<string>(Directory.EnumerateFiles(fullPath));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    total++;
                    if (entries.Count < MaxListingEntries)
                    {
                        long size = new FileInfo(file).Length;
                        entries.Add(Path.GetFileName(file) + "  (" + FormatSize(size) + ")");
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                entries.Add("[could not list: " + ex.Message + "]");
            }

            StringBuilder text = new StringBuilder();
            foreach (string entry in entries)
            {
                text.Append(entry).Append('\n');
            }

            if (total > entries.Count)
            {
                text.Append("[").Append(total - entries.Count).Append(" more entries not shown]\n");
            }

            if (total == 0)
            {
                text.Append("(empty)\n");
            }

            string body = text.ToString();
            return new FileMentionAttachment
            {
                Raw = mention.Raw,
                RelativePath = Relative(fullPath, true),
                FullPath = fullPath,
                Kind = FileMentionKindEnum.Directory,
                Text = body,
                Bytes = Encoding.UTF8.GetByteCount(body),
                Lines = total,
                Inlined = true,
                Summary = "directory, " + total.ToString(CultureInfo.InvariantCulture) + (total == 1 ? " entry" : " entries")
            };
        }

        private static string BuildBlock(List<FileMentionAttachment> attachments)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("<mentioned-files>\n");
            builder.Append("The user attached these with @ mentions. Files are shown with line numbers; a large file is shown as a structural map, so use read_file with offset and limit for the parts you need.\n");
            foreach (FileMentionAttachment attachment in attachments)
            {
                if (attachment.Kind == FileMentionKindEnum.Directory)
                {
                    builder.Append("<directory path=\"").Append(attachment.RelativePath).Append("\" entries=\"").Append(attachment.Lines).Append("\">\n");
                    builder.Append(attachment.Text);
                    builder.Append("</directory>\n");
                }
                else
                {
                    builder.Append("<file path=\"").Append(attachment.RelativePath).Append("\" lines=\"").Append(attachment.Lines)
                        .Append("\" view=\"").Append(attachment.Inlined ? "full" : "map").Append("\">\n");
                    builder.Append(attachment.Text);
                    if (!attachment.Text.EndsWith("\n", StringComparison.Ordinal)) builder.Append('\n');
                    builder.Append("</file>\n");
                }
            }

            builder.Append("</mentioned-files>");
            return builder.ToString();
        }

        private List<string> GetIndex()
        {
            lock (_IndexSync)
            {
                if (_Index != null && (DateTime.UtcNow - _IndexBuiltUtc) < TimeSpan.FromSeconds(10))
                {
                    return _Index;
                }

                List<string> index = new List<string>();
                Queue<DirectoryLevel> pending = new Queue<DirectoryLevel>();
                pending.Enqueue(new DirectoryLevel(_Root, 0));
                while (pending.Count > 0 && index.Count < MaxIndexedEntries)
                {
                    DirectoryLevel level = pending.Dequeue();
                    try
                    {
                        List<string> directories = new List<string>(Directory.EnumerateDirectories(level.Path));
                        directories.Sort(StringComparer.OrdinalIgnoreCase);
                        foreach (string directory in directories)
                        {
                            if (_SkippedDirectories.Contains(Path.GetFileName(directory))) continue;
                            index.Add(Relative(directory, true));
                            if (level.Depth + 1 < MaxIndexDepth) pending.Enqueue(new DirectoryLevel(directory, level.Depth + 1));
                        }

                        List<string> files = new List<string>(Directory.EnumerateFiles(level.Path));
                        files.Sort(StringComparer.OrdinalIgnoreCase);
                        foreach (string file in files)
                        {
                            index.Add(Relative(file, false));
                            if (index.Count >= MaxIndexedEntries) break;
                        }
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                    }
                }

                _Index = index;
                _IndexBuiltUtc = DateTime.UtcNow;
                return index;
            }
        }

        private static int Rank(string path, string needle)
        {
            string trimmed = path.TrimEnd('/');
            int slash = trimmed.LastIndexOf('/');
            string name = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            if (string.Equals(path, needle, StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, needle.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return 0;
            if (name.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) return 1;
            if (path.StartsWith(needle, StringComparison.OrdinalIgnoreCase)) return 2;
            if (name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return 3;
            if (path.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            return IsSubsequence(needle, path) ? 5 : -1;
        }

        private static bool IsSubsequence(string needle, string haystack)
        {
            int position = 0;
            foreach (char c in needle)
            {
                position = haystack.IndexOf(char.ToLowerInvariant(c).ToString(), position, StringComparison.OrdinalIgnoreCase);
                if (position < 0) return false;
                position++;
            }

            return true;
        }

        private static int CompareTopLevel(string a, string b)
        {
            bool aDirectory = a.EndsWith("/", StringComparison.Ordinal);
            bool bDirectory = b.EndsWith("/", StringComparison.Ordinal);
            if (aDirectory != bDirectory) return aDirectory ? -1 : 1;
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool EndsWithSeparator(string path)
        {
            return path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal);
        }

        private static int CountLines(string content)
        {
            if (content.Length == 0) return 0;
            int lines = 1;
            foreach (char c in content)
            {
                if (c == '\n') lines++;
            }

            return content.EndsWith("\n", StringComparison.Ordinal) ? lines - 1 : lines;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1048576) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        #endregion
    }
}
