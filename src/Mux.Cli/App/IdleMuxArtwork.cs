namespace Mux.Cli.App
{
    using System.Collections.Generic;
    using TUIKit;

    /// <summary>Enlarges the canonical block-glyph wordmark onto blank transcript cells.</summary>
    internal static class IdleMuxArtwork
    {
        private static readonly IReadOnlyList<string> _Wordmark = MuxBanner.WordmarkLines();

        internal static void Render(BufferSurface surface)
        {
            int sourceWidth = _Wordmark[0].Length;
            int sourceHalfRows = _Wordmark.Count * 2;
            // Fit within the requested one-third area without stretching the banner's proportions.
            // Half-cell vertical resolution keeps the size and the stroke edges precise when resized.
            int width = System.Math.Min(surface.Size.Width / 3, surface.Size.Height / 3 * sourceWidth / _Wordmark.Count);
            if (width < sourceWidth) return;
            int halfHeight = (width * sourceHalfRows + sourceWidth / 2) / sourceWidth;
            int height = (halfHeight + 1) / 2;
            int left = (surface.Size.Width - width) / 2;
            int top = (surface.Size.Height - height) / 2;

            // Treat each source character as two filled/empty half-cells. Scaling glyphs themselves
            // would repeat the gaps in ▀ and ▄, producing stripes instead of continuous solid strokes.
            for (int y = 0; y < height; y++)
            {
                int upperRow = 2 * y * sourceHalfRows / halfHeight;
                int lowerRow = (2 * y + 1) * sourceHalfRows / halfHeight;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = x * sourceWidth / width;
                    bool upper = Filled(sourceX, upperRow);
                    bool lower = 2 * y + 1 < halfHeight && Filled(sourceX, lowerRow);
                    if (!upper && !lower) continue;
                    Cell cell = surface.Get(left + x, top + y);
                    if (cell.Grapheme != " " || cell.IsContinuation) continue;
                    string glyph = upper ? (lower ? "█" : "▀") : "▄";
                    surface.Set(left + x, top + y,
                        Cell.Glyph(glyph, cell.Style.WithForeground(Color.FromRgb(32, 32, 32)), 1));
                }
            }
        }

        private static bool Filled(int x, int halfRow)
        {
            char glyph = _Wordmark[halfRow / 2][x];
            return glyph == '█' || (glyph == '▀' && halfRow % 2 == 0) || (glyph == '▄' && halfRow % 2 != 0);
        }
    }
}
