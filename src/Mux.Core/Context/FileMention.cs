namespace Mux.Core.Context
{
    /// <summary>
    /// One <c>@path</c> mention found in a prompt: where it is, how it was typed, and the path it names.
    /// </summary>
    public sealed class FileMention
    {
        #region Public-Members

        /// <summary>The mention exactly as typed, including the leading <c>@</c> and any quotes.</summary>
        public string Raw { get; set; } = string.Empty;

        /// <summary>The path the mention names, without the <c>@</c>, quotes, or trailing punctuation.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>The zero-based index of the <c>@</c> in the prompt.</summary>
        public int Start { get; set; }

        /// <summary>The length of <see cref="Raw"/> in the prompt.</summary>
        public int Length { get; set; }

        /// <summary>Whether the path was written in quotes (<c>@"path with spaces"</c>).</summary>
        public bool Quoted { get; set; }

        /// <summary>Whether the path ends with a separator, asking for a directory listing.</summary>
        public bool IsDirectoryHint { get; set; }

        #endregion
    }
}
