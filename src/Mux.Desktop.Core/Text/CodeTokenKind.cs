namespace Mux.Desktop.Text
{
    /// <summary>
    /// The classification of a single token produced by <see cref="CodeTokenizer"/>. The UI maps each kind to
    /// a color when rendering a fenced code block.
    /// </summary>
    public enum CodeTokenKind
    {
        /// <summary>Ordinary text with no special coloring (identifiers, punctuation, whitespace).</summary>
        Plain,

        /// <summary>A language keyword.</summary>
        Keyword,

        /// <summary>A string or character literal, including its delimiters.</summary>
        StringLiteral,

        /// <summary>A line or block comment, including its markers.</summary>
        Comment,

        /// <summary>A numeric literal.</summary>
        Number
    }
}
