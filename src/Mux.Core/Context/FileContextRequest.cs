namespace Mux.Core.Context
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A request to turn a file into a model-context block: the file, the mode, the size gate, and optional
    /// caller-supplied structure. The builder reads nothing from disk — the caller supplies the content.
    /// </summary>
    public sealed class FileContextRequest
    {
        /// <summary>The file path, used for the map note and outline heuristics. Never null.</summary>
        public string Path { get; }

        /// <summary>The full file contents. Never null.</summary>
        public string Content { get; }

        /// <summary>The mode applied when the file exceeds <see cref="InlineThresholdBytes"/>.</summary>
        public FileContextMode Mode { get; }

        /// <summary>The byte size at or below which the file is inlined whole. Floored at 1.</summary>
        public int InlineThresholdBytes { get; }

        /// <summary>How many leading lines a map or truncation includes. Floored at 0.</summary>
        public int HeadLines { get; }

        /// <summary>How many lines per chunk the summarizer uses. Floored at 1.</summary>
        public int SummaryChunkLines { get; }

        /// <summary>A caller-supplied outline (for example language-server symbols); null uses the provider.</summary>
        public IReadOnlyList<FileOutlineEntry>? Outline { get; }

        /// <summary>A key identifying the summarizing model, for cache addressing. Never null (may be empty).</summary>
        public string ModelKey { get; }

        /// <summary>
        /// Creates a file-context request.
        /// </summary>
        /// <param name="path">The file path; null becomes empty.</param>
        /// <param name="content">The full file contents; null becomes empty.</param>
        /// <param name="mode">The mode applied when the file is large.</param>
        /// <param name="inlineThresholdBytes">The inline size gate in bytes (floored at 1).</param>
        /// <param name="headLines">Leading lines to include in a map/truncation (floored at 0).</param>
        /// <param name="summaryChunkLines">Lines per summarizer chunk (floored at 1).</param>
        /// <param name="outline">A caller-supplied outline, or null to use the generic provider.</param>
        /// <param name="modelKey">A key identifying the summarizing model; null becomes empty.</param>
        public FileContextRequest(
            string? path,
            string? content,
            FileContextMode mode,
            int inlineThresholdBytes,
            int headLines,
            int summaryChunkLines,
            IReadOnlyList<FileOutlineEntry>? outline,
            string? modelKey)
        {
            Path = path ?? string.Empty;
            Content = content ?? string.Empty;
            Mode = mode;
            InlineThresholdBytes = Math.Max(1, inlineThresholdBytes);
            HeadLines = Math.Max(0, headLines);
            SummaryChunkLines = Math.Max(1, summaryChunkLines);
            Outline = outline;
            ModelKey = modelKey ?? string.Empty;
        }
    }
}
