namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Helpers for rendering modal footer hints without truncation. A hint is a run of " · "-separated
    /// key/action segments; <see cref="Wrap"/> packs them into as few lines as fit the available width so a
    /// long hint spills onto another line instead of being silently cut off by the caller's trim.
    /// </summary>
    internal static class HintText
    {
        private const string Separator = " · ";

        /// <summary>
        /// Splits a " · "-separated hint into lines that each fit within <paramref name="width"/> columns,
        /// greedily packing whole segments and never splitting one. Widths are measured in UTF-16 units to
        /// match the trim the modals apply when drawing. Always returns at least one line, even for empty
        /// input, so callers can render and size uniformly.
        /// </summary>
        /// <param name="hint">The full hint text. Null is treated as empty.</param>
        /// <param name="width">The available width in columns. Values below one are treated as one.</param>
        /// <returns>The wrapped lines; never null and never empty.</returns>
        public static IReadOnlyList<string> Wrap(string? hint, int width)
        {
            int limit = Math.Max(1, width);
            string[] segments = (hint ?? string.Empty).Split(new[] { Separator }, StringSplitOptions.None);

            List<string> lines = new List<string>();
            string current = string.Empty;
            bool hasCurrent = false;

            foreach (string segment in segments)
            {
                if (!hasCurrent)
                {
                    current = segment;
                    hasCurrent = true;
                    continue;
                }

                string candidate = current + Separator + segment;
                if (candidate.Length <= limit)
                {
                    current = candidate;
                }
                else
                {
                    // The packed line is full; start a new one. A single segment wider than the limit still
                    // lands on its own line and is left for the caller to trim.
                    lines.Add(current);
                    current = segment;
                }
            }

            lines.Add(current);
            return lines;
        }
    }
}
