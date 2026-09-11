namespace Mux.Desktop.Text
{
    /// <summary>
    /// A single classified span of source text produced by <see cref="CodeTokenizer"/>. Concatenating every
    /// token's <see cref="Text"/> in order reproduces the original input exactly (the tokenizer is lossless).
    /// </summary>
    public readonly struct CodeToken
    {
        /// <summary>
        /// Instantiate a token.
        /// </summary>
        /// <param name="text">The verbatim text of the span.</param>
        /// <param name="kind">The classification of the span.</param>
        public CodeToken(string text, CodeTokenKind kind)
        {
            Text = text ?? string.Empty;
            Kind = kind;
        }

        /// <summary>
        /// The verbatim text of the span.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// The classification of the span.
        /// </summary>
        public CodeTokenKind Kind { get; }
    }
}
