namespace Mux.Cli.App
{
    using System.Collections.Generic;
    using Mux.Core.Branding;
    using TUIKit;

    /// <summary>Enlarges the canonical block-glyph wordmark onto blank transcript cells.</summary>
    internal static class IdleMuxArtwork
    {
        /// <summary>The faint colour of the artwork: its glyphs' foreground, or its cells' background.</summary>
        internal static readonly Color Faint = Color.FromRgb(32, 32, 32);

        private static readonly IReadOnlyList<string> _Wordmark = MuxBanner.WordmarkLines();
        private static readonly bool[,] _HalfCells = BlockArt.HalfCells(_Wordmark);
        private static readonly BlockArtMode _Mode = BlockArt.TerminalMode();

        /// <summary>Draws the artwork in the mode this terminal needs (see <see cref="BlockArt.TerminalMode()"/>).</summary>
        /// <param name="surface">The transcript area.</param>
        internal static void Render(BufferSurface surface)
        {
            Render(surface, _Mode);
        }

        /// <summary>Draws the artwork centred in the surface, about one third of its size, over blank cells only.</summary>
        /// <param name="surface">The transcript area.</param>
        /// <param name="mode">Glyphs draws half-block glyphs at half-cell resolution; Cells draws background-coloured
        /// spaces at whole-cell resolution, for terminals whose font block glyphs leave gaps between rows.</param>
        internal static void Render(BufferSurface surface, BlockArtMode mode)
        {
            int sourceWidth = _HalfCells.GetLength(1);
            int sourceHalfRows = _HalfCells.GetLength(0);
            // Fit within the requested one-third area without stretching the banner's proportions.
            // Half-cell vertical resolution keeps the size and the stroke edges precise when resized.
            int width = System.Math.Min(surface.Size.Width / 3, surface.Size.Height / 3 * sourceWidth / _Wordmark.Count);
            if (width < sourceWidth) return;
            int halfHeight = (width * sourceHalfRows + sourceWidth / 2) / sourceWidth;
            int height = (halfHeight + 1) / 2;
            int left = (surface.Size.Width - width) / 2;
            int top = (surface.Size.Height - height) / 2;

            for (int y = 0; y < height; y++)
            {
                int upperRow = 2 * y * sourceHalfRows / halfHeight;
                int lowerRow = (2 * y + 1) * sourceHalfRows / halfHeight;
                // In cell mode each terminal row samples the art at the row's centre, so a row is either filled or
                // not, with no half-block glyphs (whose font gaps would show as bands).
                int centreRow = (2 * y + 1) * sourceHalfRows / (2 * height);
                for (int x = 0; x < width; x++)
                {
                    int sourceX = x * sourceWidth / width;
                    bool upper;
                    bool lower;
                    if (mode == BlockArtMode.Cells)
                    {
                        upper = _HalfCells[centreRow, sourceX];
                        lower = false;
                    }
                    else
                    {
                        // Treat each source character as two filled/empty half-cells. Scaling glyphs themselves
                        // would repeat the gaps in ▀ and ▄, producing stripes instead of continuous solid strokes.
                        upper = _HalfCells[upperRow, sourceX];
                        lower = 2 * y + 1 < halfHeight && _HalfCells[lowerRow, sourceX];
                    }

                    if (!upper && !lower) continue;
                    Cell cell = surface.Get(left + x, top + y);
                    if (cell.Grapheme != " " || cell.IsContinuation) continue;
                    if (mode == BlockArtMode.Cells)
                    {
                        surface.Set(left + x, top + y, Cell.Glyph(" ", cell.Style.WithBackground(Faint), 1));
                        continue;
                    }

                    string glyph = upper ? (lower ? "█" : "▀") : "▄";
                    surface.Set(left + x, top + y, Cell.Glyph(glyph, cell.Style.WithForeground(Faint), 1));
                }
            }
        }
    }
}
