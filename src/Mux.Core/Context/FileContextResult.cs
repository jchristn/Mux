namespace Mux.Core.Context
{
    /// <summary>
    /// The outcome of building a file-context block.
    /// </summary>
    public sealed class FileContextResult
    {
        /// <summary>The text block to feed the model. Never null.</summary>
        public string Text { get; }

        /// <summary>The mode actually used (for example <see cref="FileContextMode.Map"/> when a requested summarize fell back).</summary>
        public FileContextMode Mode { get; }

        /// <summary>Whether the file was small enough to be inlined whole (mode did not apply).</summary>
        public bool Inlined { get; }

        /// <summary>The number of outline entries emitted (0 when inlined or truncated).</summary>
        public int OutlineEntryCount { get; }

        /// <summary>Whether a summary came from the cache rather than a fresh model call.</summary>
        public bool FromCache { get; }

        /// <summary>
        /// Creates a file-context result.
        /// </summary>
        /// <param name="text">The block text; null becomes empty.</param>
        /// <param name="mode">The mode actually used.</param>
        /// <param name="inlined">Whether the file was inlined whole.</param>
        /// <param name="outlineEntryCount">The number of outline entries emitted.</param>
        /// <param name="fromCache">Whether a summary was served from cache.</param>
        public FileContextResult(string? text, FileContextMode mode, bool inlined, int outlineEntryCount, bool fromCache)
        {
            Text = text ?? string.Empty;
            Mode = mode;
            Inlined = inlined;
            OutlineEntryCount = outlineEntryCount;
            FromCache = fromCache;
        }
    }
}
