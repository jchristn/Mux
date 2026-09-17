namespace Mux.Core.Context
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Prompting;

    /// <summary>
    /// The iterative map-reduce file summarizer: it chunks a file by a line budget, takes dense notes on each
    /// chunk (the <c>file.summary.map</c> prompt), and reduces the notes into a final navigable summary (the
    /// <c>file.summary.reduce</c> prompt). The model calls go through an injected sidecar delegate — the same
    /// tools-off, temperature-0, non-streaming pattern conversation compaction uses — so this type has no LLM
    /// dependency of its own. Results are cached by content hash so an unchanged file is not re-summarized.
    /// </summary>
    public sealed class FileSummarizer : IFileSummarizer
    {
        private readonly Func<string, string, CancellationToken, Task<string>> _Sidecar;
        private readonly FileSummaryCache? _Cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="FileSummarizer"/> class.
        /// </summary>
        /// <param name="sidecar">The sidecar call: given a system prompt and a user prompt, returns the model's text. Must not be null.</param>
        /// <param name="cache">An optional summary cache; null disables caching.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="sidecar"/> is null.</exception>
        public FileSummarizer(Func<string, string, CancellationToken, Task<string>> sidecar, FileSummaryCache? cache)
        {
            _Sidecar = sidecar ?? throw new ArgumentNullException(nameof(sidecar));
            _Cache = cache;
        }

        /// <inheritdoc/>
        public async Task<string> SummarizeAsync(string path, string content, int chunkLines, string modelKey, CancellationToken cancellationToken)
        {
            string safeContent = content ?? string.Empty;
            string key = FileSummaryCache.BuildKey(path, FileSummaryCache.ComputeContentHash(safeContent), "summarize", modelKey);
            if (_Cache != null && _Cache.TryGet(key, out string cached) && cached.Length > 0)
            {
                return cached;
            }

            int budget = Math.Max(1, chunkLines);
            string[] lines = safeContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

            string mapSystem = PromptResolver.Shared.GetEffective("file.summary.map");
            List<string> notes = new List<string>();
            for (int start = 0; start < lines.Length; start += budget)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int end = Math.Min(lines.Length, start + budget);
                string chunk = BuildNumberedChunk(lines, start, end);
                string note = await _Sidecar(mapSystem, chunk, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(note))
                {
                    notes.Add(note.Trim());
                }
            }

            string summary;
            if (notes.Count == 0)
            {
                summary = string.Empty;
            }
            else if (notes.Count == 1)
            {
                summary = notes[0];
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
                string reduceSystem = PromptResolver.Shared.GetEffective("file.summary.reduce");
                summary = (await _Sidecar(reduceSystem, string.Join("\n\n", notes), cancellationToken).ConfigureAwait(false)).Trim();
            }

            if (_Cache != null && summary.Length > 0)
            {
                _Cache.Put(key, summary);
            }

            return summary;
        }

        private static string BuildNumberedChunk(string[] lines, int startIndex, int endIndex)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("Lines ").Append(startIndex + 1).Append('-').Append(endIndex).Append(":").Append('\n');
            for (int i = startIndex; i < endIndex; i++)
            {
                builder.Append((i + 1).ToString().PadLeft(6)).Append('\t').Append(lines[i]).Append('\n');
            }

            return builder.ToString();
        }
    }
}
