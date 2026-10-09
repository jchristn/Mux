namespace Mux.Core.Context
{
    /// <summary>
    /// A resolved <c>@path</c> mention and the context block built for it.
    /// </summary>
    public sealed class FileMentionAttachment
    {
        #region Public-Members

        /// <summary>The mention as typed.</summary>
        public string Raw { get; set; } = string.Empty;

        /// <summary>The path relative to the working directory, with forward slashes (directories end with <c>/</c>).</summary>
        public string RelativePath { get; set; } = string.Empty;

        /// <summary>The absolute path.</summary>
        public string FullPath { get; set; } = string.Empty;

        /// <summary>Whether this is a file or a directory.</summary>
        public FileMentionKindEnum Kind { get; set; } = FileMentionKindEnum.File;

        /// <summary>The attached text (numbered lines, a structural map, or a listing).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>The UTF-8 size of <see cref="Text"/>.</summary>
        public int Bytes { get; set; }

        /// <summary>The number of lines in the file, or entries in the directory listing.</summary>
        public int Lines { get; set; }

        /// <summary>Whether the whole file was attached (false when a structural map stands in for a large file).</summary>
        public bool Inlined { get; set; } = true;

        /// <summary>A short human-readable description, for example <c>full, 120 lines</c> or <c>map, 4200 lines</c>.</summary>
        public string Summary { get; set; } = string.Empty;

        #endregion
    }
}
