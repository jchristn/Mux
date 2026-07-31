namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Formats the command-menu rows into three columns — title, key chord, and <c>/slash</c> aliases — so
    /// that the chord column and the alias column each begin on a single vertical axis across every row. The
    /// three input lists are parallel and must be the same length.
    /// </summary>
    public static class CommandMenuFormatter
    {
        /// <summary>
        /// Builds the aligned rows. The chord column is padded to the widest chord and the title column to the
        /// widest title, so both the chords and the aliases line up vertically. Trailing padding is trimmed.
        /// </summary>
        /// <param name="titles">The command titles.</param>
        /// <param name="chords">The key chords (empty string when a command has none).</param>
        /// <param name="aliases">The pre-joined <c>/slash</c> alias text (empty string when none).</param>
        /// <returns>One aligned label per input row.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the lists are not the same length.</exception>
        public static List<string> AlignRows(
            IReadOnlyList<string> titles,
            IReadOnlyList<string> chords,
            IReadOnlyList<string> aliases)
        {
            if (titles == null) throw new ArgumentNullException(nameof(titles));
            if (chords == null) throw new ArgumentNullException(nameof(chords));
            if (aliases == null) throw new ArgumentNullException(nameof(aliases));
            if (titles.Count != chords.Count || titles.Count != aliases.Count)
            {
                throw new ArgumentException("Titles, chords, and aliases must be the same length.");
            }

            int titleWidth = 0;
            int chordWidth = 0;
            for (int i = 0; i < titles.Count; i++)
            {
                titleWidth = Math.Max(titleWidth, (titles[i] ?? string.Empty).Length);
                chordWidth = Math.Max(chordWidth, (chords[i] ?? string.Empty).Length);
            }

            List<string> rows = new List<string>(titles.Count);
            for (int i = 0; i < titles.Count; i++)
            {
                string row = (titles[i] ?? string.Empty).PadRight(titleWidth)
                    + "  " + (chords[i] ?? string.Empty).PadRight(chordWidth)
                    + "  " + (aliases[i] ?? string.Empty);
                rows.Add(row.TrimEnd());
            }

            return rows;
        }
    }
}
