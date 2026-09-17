namespace Mux.Core.Context
{
    using System;

    /// <summary>
    /// One line-anchored entry in a file's structural outline: a titled span of lines the model can read
    /// directly with <c>read_file(offset, limit)</c>. Line numbers are 1-based and inclusive.
    /// </summary>
    public sealed class FileOutlineEntry
    {
        /// <summary>The entry's title (a heading, a top-level declaration, or a block's first line). Never null.</summary>
        public string Title { get; }

        /// <summary>The 1-based line where the entry starts.</summary>
        public int StartLine { get; }

        /// <summary>The 1-based line where the entry ends (inclusive).</summary>
        public int EndLine { get; }

        /// <summary>The nesting depth (0 for top-level; deeper for sub-headings). Never negative.</summary>
        public int Depth { get; }

        /// <summary>
        /// Creates an outline entry.
        /// </summary>
        /// <param name="title">The entry title; null becomes empty.</param>
        /// <param name="startLine">The 1-based start line (floored at 1).</param>
        /// <param name="endLine">The 1-based inclusive end line (floored at <paramref name="startLine"/>).</param>
        /// <param name="depth">The nesting depth (floored at 0).</param>
        public FileOutlineEntry(string? title, int startLine, int endLine, int depth)
        {
            Title = (title ?? string.Empty).Trim();
            StartLine = Math.Max(1, startLine);
            EndLine = Math.Max(StartLine, endLine);
            Depth = Math.Max(0, depth);
        }
    }
}
