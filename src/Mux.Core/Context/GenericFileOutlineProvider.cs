namespace Mux.Core.Context
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;

    /// <summary>
    /// A single, language-agnostic outline provider. Heading-bearing files (Markdown and similar) parse by
    /// their headings; everything else parses by top-level (column-0) declaration lines, falling back to
    /// blank-line-delimited blocks when a file has no clear top-level structure. This is deliberately coarse:
    /// it beats truncation because every entry is line-anchored, and it will never be as precise as a language
    /// server — the <see cref="IFileOutlineProvider"/> seam exists so a per-language parser can replace it
    /// later without touching the builder or any surface.
    /// </summary>
    public sealed class GenericFileOutlineProvider : IFileOutlineProvider
    {
        private const int MaxEntries = 300;
        private const int MaxTitleLength = 100;

        private static readonly Regex _Heading = new Regex(@"^\s{0,3}(#{1,6})\s+(\S.*)$", RegexOptions.Compiled);

        /// <inheritdoc/>
        public IReadOnlyList<FileOutlineEntry> GetOutline(string path, string content)
        {
            string[] lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            List<FileOutlineEntry> headings = BuildHeadingOutline(lines);
            if (headings.Count >= 2)
            {
                return Cap(headings);
            }

            List<FileOutlineEntry> topLevel = BuildTopLevelOutline(lines);
            if (topLevel.Count >= 2)
            {
                return Cap(topLevel);
            }

            return Cap(BuildBlockOutline(lines));
        }

        private static List<FileOutlineEntry> BuildHeadingOutline(string[] lines)
        {
            List<int> headingRows = new List<int>();
            List<string> titles = new List<string>();
            List<int> depths = new List<int>();

            for (int i = 0; i < lines.Length; i++)
            {
                Match match = _Heading.Match(lines[i]);
                if (match.Success)
                {
                    headingRows.Add(i);
                    depths.Add(match.Groups[1].Value.Length);
                    titles.Add(match.Groups[2].Value);
                }
            }

            List<FileOutlineEntry> entries = new List<FileOutlineEntry>();
            for (int h = 0; h < headingRows.Count; h++)
            {
                int start = headingRows[h] + 1;
                int end = h + 1 < headingRows.Count ? headingRows[h + 1] : lines.Length;
                entries.Add(new FileOutlineEntry(Truncate(titles[h]), start, end, depths[h] - 1));
            }

            return entries;
        }

        private static List<FileOutlineEntry> BuildTopLevelOutline(string[] lines)
        {
            List<int> rows = new List<int>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                {
                    continue;
                }

                if (!LooksLikeDeclaration(line))
                {
                    continue;
                }

                rows.Add(i);
            }

            List<FileOutlineEntry> entries = new List<FileOutlineEntry>();
            for (int r = 0; r < rows.Count; r++)
            {
                int start = rows[r] + 1;
                int end = r + 1 < rows.Count ? rows[r + 1] : lines.Length;
                entries.Add(new FileOutlineEntry(Truncate(lines[rows[r]].Trim()), start, end, 0));
            }

            return entries;
        }

        private static List<FileOutlineEntry> BuildBlockOutline(string[] lines)
        {
            List<FileOutlineEntry> entries = new List<FileOutlineEntry>();
            int blockStart = -1;
            string firstLine = string.Empty;

            for (int i = 0; i < lines.Length; i++)
            {
                bool blank = lines[i].Trim().Length == 0;
                if (blank)
                {
                    if (blockStart >= 0)
                    {
                        entries.Add(new FileOutlineEntry(Truncate(firstLine.Trim()), blockStart + 1, i, 0));
                        blockStart = -1;
                    }
                }
                else if (blockStart < 0)
                {
                    blockStart = i;
                    firstLine = lines[i];
                }
            }

            if (blockStart >= 0)
            {
                entries.Add(new FileOutlineEntry(Truncate(firstLine.Trim()), blockStart + 1, lines.Length, 0));
            }

            return entries;
        }

        private static bool LooksLikeDeclaration(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                return false;
            }

            // Skip lone brackets and comment/preprocessor/import noise that would clutter a top-level outline.
            char first = trimmed[0];
            if (first == '}' || first == ')' || first == ']' || first == '{')
            {
                return false;
            }

            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal)
                || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private static string Truncate(string text)
        {
            if (text.Length <= MaxTitleLength)
            {
                return text;
            }

            return text.Substring(0, MaxTitleLength - 1) + "…";
        }

        private static List<FileOutlineEntry> Cap(List<FileOutlineEntry> entries)
        {
            if (entries.Count <= MaxEntries)
            {
                return entries;
            }

            return entries.GetRange(0, MaxEntries);
        }
    }
}
