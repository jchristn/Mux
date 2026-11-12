namespace Mux.Core.Branding
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Turns block-character art (<c>█</c>, <c>▀</c>, <c>▄</c>) into filled rectangles, so a GUI can draw the mux
    /// wordmark as shapes instead of text. Drawn as text, the art breaks into bands: a font's block glyphs are
    /// shorter than its line height (Menlo's full block is 2088 of 2384 units, leaving about 12% of every line
    /// empty) and may be narrower than the character advance, so gaps show between rows and sometimes between
    /// columns. A terminal stretches the glyphs to fill each cell, which is why the TUI banner looks solid.
    /// Coordinates are in cells horizontally and half-rows vertically (a row is two half-rows); horizontally and
    /// vertically touching blocks are merged so rectangles share no internal edges.
    /// </summary>
    public static class BlockArt
    {
        #region Public-Members

        /// <summary>The mux wordmark, as drawn by the TUI banner and the About windows.</summary>
        public static IReadOnlyList<string> Wordmark { get; } = new[]
        {
            "▄▄   ▄▄ ▄▄ ▄▄ ▄▄ ▄▄",
            "██▀▄▀██ ██ ██ ▀█▄█▀",
            "██   ██ ▀███▀ ██ ██"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The filled rectangles for block-character art. Characters other than <c>█</c>, <c>▀</c>, and <c>▄</c> are
        /// blank.
        /// </summary>
        /// <param name="lines">The art, one string per row.</param>
        /// <returns>Rectangles as (column, half-row, width in cells, height in half-rows).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
        public static IReadOnlyList<(int X, int Y, int Width, int Height)> Rectangles(IReadOnlyList<string> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            bool[,] filled = Grid(lines, out int columns, out int halfRows);

            // Greedy merge: take the first unclaimed filled cell, extend right as far as cells are filled, then extend
            // down while the whole span below is filled and unclaimed.
            bool[,] claimed = new bool[halfRows, columns];
            List<(int, int, int, int)> rectangles = new List<(int, int, int, int)>();
            for (int y = 0; y < halfRows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    if (!filled[y, x] || claimed[y, x]) continue;
                    int width = 1;
                    while (x + width < columns && filled[y, x + width] && !claimed[y, x + width]) width++;
                    int height = 1;
                    while (y + height < halfRows && SpanFree(filled, claimed, y + height, x, width)) height++;
                    for (int dy = 0; dy < height; dy++)
                        for (int dx = 0; dx < width; dx++) claimed[y + dy, x + dx] = true;
                    rectangles.Add((x, y, width, height));
                }
            }

            return rectangles;
        }

        /// <summary>The art's size: the widest row in cells, and the row count times two in half-rows.</summary>
        /// <param name="lines">The art, one string per row.</param>
        /// <returns>(columns, half-rows).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
        public static (int Columns, int HalfRows) Size(IReadOnlyList<string> lines)
        {
            ArgumentNullException.ThrowIfNull(lines);
            Grid(lines, out int columns, out int halfRows);
            return (columns, halfRows);
        }

        #endregion

        #region Private-Methods

        // The art as a grid of filled half-cells: [half-row, column].
        private static bool[,] Grid(IReadOnlyList<string> lines, out int columns, out int halfRows)
        {
            columns = 0;
            foreach (string line in lines) columns = Math.Max(columns, (line ?? string.Empty).Length);
            halfRows = lines.Count * 2;
            bool[,] filled = new bool[halfRows, columns];
            for (int row = 0; row < lines.Count; row++)
            {
                string line = lines[row] ?? string.Empty;
                for (int x = 0; x < line.Length; x++)
                {
                    char c = line[x];
                    filled[row * 2, x] = c == '█' || c == '▀';
                    filled[(row * 2) + 1, x] = c == '█' || c == '▄';
                }
            }

            return filled;
        }

        private static bool SpanFree(bool[,] filled, bool[,] claimed, int y, int x, int width)
        {
            for (int dx = 0; dx < width; dx++)
            {
                if (!filled[y, x + dx] || claimed[y, x + dx]) return false;
            }

            return true;
        }

        #endregion
    }
}
