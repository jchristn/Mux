namespace Mux.Core.Context
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Prompting;

    /// <summary>
    /// Turns a file plus a mode into a context block. Small files inline whole; large files follow the mode:
    /// <see cref="FileContextMode.Map"/> emits a structural outline with line ranges plus the first lines,
    /// <see cref="FileContextMode.Summarize"/> emits an iterative summary (falling back to a map when no
    /// summarizer is supplied), and <see cref="FileContextMode.Truncate"/> preserves a head slice. The builder
    /// reads nothing from disk and does not itself call a model — the summarizer is injected — so the map and
    /// truncate paths are pure and synchronous under the hood.
    /// </summary>
    public sealed class FileContextBuilder
    {
        private readonly IFileOutlineProvider _OutlineProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileContextBuilder"/> class.
        /// </summary>
        /// <param name="outlineProvider">The outline provider; null uses <see cref="GenericFileOutlineProvider"/>.</param>
        public FileContextBuilder(IFileOutlineProvider? outlineProvider)
        {
            _OutlineProvider = outlineProvider ?? new GenericFileOutlineProvider();
        }

        /// <summary>
        /// Builds a context block for a request.
        /// </summary>
        /// <param name="request">The request; must not be null.</param>
        /// <param name="summarizer">The summarizer used only for <see cref="FileContextMode.Summarize"/>; null falls back to a map.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The built block; never null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
        public async Task<FileContextResult> BuildAsync(FileContextRequest request, IFileSummarizer? summarizer, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            int bytes = Encoding.UTF8.GetByteCount(request.Content);
            if (bytes <= request.InlineThresholdBytes)
            {
                return new FileContextResult(NumberLines(request.Content, 1, -1), request.Mode, true, 0, false);
            }

            switch (request.Mode)
            {
                case FileContextMode.Truncate:
                    return BuildTruncated(request);
                case FileContextMode.Summarize:
                    if (summarizer != null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string summary = await summarizer.SummarizeAsync(request.Path, request.Content, request.SummaryChunkLines, request.ModelKey, cancellationToken).ConfigureAwait(false);
                        return BuildSummary(request, summary);
                    }

                    return BuildMap(request);
                default:
                    return BuildMap(request);
            }
        }

        private FileContextResult BuildMap(FileContextRequest request)
        {
            IReadOnlyList<FileOutlineEntry> outline = request.Outline ?? _OutlineProvider.GetOutline(request.Path, request.Content);

            StringBuilder builder = new StringBuilder();
            builder.Append(MapNote(request.Path)).Append('\n').Append('\n');
            builder.Append("Structural map (").Append(outline.Count).Append(" entries):").Append('\n');
            foreach (FileOutlineEntry entry in outline)
            {
                for (int i = 0; i < entry.Depth && i < 6; i++)
                {
                    builder.Append("  ");
                }

                builder.Append("lines ").Append(entry.StartLine).Append('-').Append(entry.EndLine).Append(": ").Append(entry.Title).Append('\n');
            }

            if (request.HeadLines > 0)
            {
                builder.Append('\n').Append("First ").Append(request.HeadLines).Append(" lines:").Append('\n');
                builder.Append(NumberLines(request.Content, 1, request.HeadLines));
            }

            return new FileContextResult(builder.ToString(), FileContextMode.Map, false, outline.Count, false);
        }

        private FileContextResult BuildSummary(FileContextRequest request, string summary)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(MapNote(request.Path)).Append('\n').Append('\n');
            builder.Append("Summary:").Append('\n').Append((summary ?? string.Empty).Trim());
            return new FileContextResult(builder.ToString(), FileContextMode.Summarize, false, 0, false);
        }

        private static FileContextResult BuildTruncated(FileContextRequest request)
        {
            int head = request.HeadLines > 0 ? request.HeadLines : 200;
            StringBuilder builder = new StringBuilder();
            builder.Append("The file ").Append(request.Path).Append(" is large; showing the first ").Append(head).Append(" lines. Use read_file with offset and limit to read further.").Append('\n').Append('\n');
            builder.Append(NumberLines(request.Content, 1, head));
            return new FileContextResult(builder.ToString(), FileContextMode.Truncate, false, 0, false);
        }

        private static string MapNote(string path)
        {
            return PromptResolver.Shared.Resolve("file.map.note", new Dictionary<string, string> { { "{Path}", path } });
        }

        private static string NumberLines(string content, int startLine, int maxLines)
        {
            string[] lines = (content ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int count = maxLines > 0 ? Math.Min(maxLines, lines.Length) : lines.Length;
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                int lineNumber = startLine + i;
                builder.Append(lineNumber.ToString().PadLeft(6)).Append('\t').Append(lines[i]).Append('\n');
            }

            return builder.ToString();
        }
    }
}
