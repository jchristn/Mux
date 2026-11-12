namespace Mux.Cli.App
{
    using System.Collections.Generic;
    using System.Text;
    using Mux.Core.Branding;

    /// <summary>
    /// The mux ASCII-art wordmark and the startup splash text.
    /// </summary>
    public static class MuxBanner
    {
        /// <summary>
        /// The ASCII-art wordmark rows on their own, without the tagline/copyright/URL splash text. This is
        /// <see cref="BlockArt.Wordmark"/>, the same art the desktop app draws as rectangles.
        /// </summary>
        /// <returns>The wordmark lines.</returns>
        public static IReadOnlyList<string> WordmarkLines()
        {
            return new List<string>(BlockArt.Wordmark);
        }

        /// <summary>
        /// The wordmark as lines to print to the normal screen (the scrollback). In glyph mode these are the block
        /// glyph rows. In cell mode each art half-row is one line and each art column two cells, drawn as spaces in
        /// reverse video (so their background is the terminal's current text colour) and reset at the end of each
        /// line; the 19x3 wordmark becomes 38 columns by 6 rows.
        /// </summary>
        /// <param name="mode">How the terminal should draw block art.</param>
        /// <returns>The lines to write, one per row.</returns>
        public static IReadOnlyList<string> ScrollbackLines(BlockArtMode mode)
        {
            if (mode != BlockArtMode.Cells) return WordmarkLines();
            return AnsiCellLines(BlockArt.Wordmark);
        }

        /// <summary>
        /// Block art as ANSI lines of reverse-video spaces at twice its size (see <see cref="BlockArt.TerminalCells"/>).
        /// Filled runs start with <c>ESC[7m</c> and end with <c>ESC[27m</c>; every line ends with <c>ESC[0m</c>.
        /// </summary>
        /// <param name="art">The art, one string per row.</param>
        /// <returns>One ANSI line per terminal row.</returns>
        public static IReadOnlyList<string> AnsiCellLines(IReadOnlyList<string> art)
        {
            bool[,] cells = BlockArt.TerminalCells(art);
            List<string> lines = new List<string>();
            for (int y = 0; y < cells.GetLength(0); y++)
            {
                StringBuilder line = new StringBuilder();
                bool on = false;
                for (int x = 0; x < cells.GetLength(1); x++)
                {
                    if (cells[y, x] != on)
                    {
                        line.Append(cells[y, x] ? "\u001b[7m" : "\u001b[27m");
                        on = cells[y, x];
                    }

                    line.Append(' ');
                }

                if (on) line.Append("\u001b[27m");
                line.Append("\u001b[0m");
                lines.Add(line.ToString());
            }

            return lines;
        }

        /// <summary>
        /// Builds the startup splash lines: the wordmark, a tagline with the version, the copyright, and the
        /// project URL. The splash modal appends its own blank gap before the "press any key" hint, so the URL
        /// is the final content line here and reads with one blank line above and below it.
        /// </summary>
        /// <param name="version">The product version string.</param>
        /// <returns>The splash content lines.</returns>
        public static IReadOnlyList<string> SplashLines(string version)
        {
            List<string> lines = new List<string>(BlockArt.Wordmark);
            lines.AddRange(SplashText(version));
            return lines;
        }

        /// <summary>
        /// The splash lines that follow the wordmark: a blank line, the tagline with the version, the copyright, a
        /// blank line, and the project URL. Used when the wordmark is drawn as cells above them.
        /// </summary>
        /// <param name="version">The product version string.</param>
        /// <returns>The splash text lines.</returns>
        public static IReadOnlyList<string> SplashText(string version)
        {
            return new List<string>
            {
                string.Empty,
                "AI agent for local and remote LLMs  ·  v" + (version ?? string.Empty),
                "(c)2026 Joel Christner",
                string.Empty,
                "https://github.com/jchristn/mux"
            };
        }
    }
}
