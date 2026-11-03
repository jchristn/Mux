namespace Mux.Cli.App
{
    using System;
    using TUIKit;

    /// <summary>Scalable, faint ASCII watermark painted only onto blank transcript cells.</summary>
    internal static class IdleMuxArtwork
    {
        private static readonly string[] Mask =
        {
            "M   M  U   U  X   X",
            "MM MM  U   U  X   X",
            "M M M  U   U   X X ",
            "M M M  U   U    X  ",
            "M   M  U   U   X X ",
            "M   M  U   U  X   X",
            "M   M   UUU   X   X"
        };

        internal static void Render(BufferSurface surface)
        {
            int width = surface.Size.Width / 3;
            int height = surface.Size.Height / 3;
            if (width < Mask[0].Length || height < 3) return;
            int left = (surface.Size.Width - width) / 2;
            int top = (surface.Size.Height - height) / 2;
            for (int y = 0; y < height; y++)
            {
                int row = height == 1 ? 0 : (int)Math.Round(y * (Mask.Length - 1.0) / (height - 1));
                for (int x = 0; x < width; x++)
                {
                    char glyph = Mask[row][x * Mask[0].Length / width];
                    Cell cell = surface.Get(left + x, top + y);
                    if (glyph != ' ' && cell.Grapheme == " " && !cell.IsContinuation)
                        surface.Set(left + x, top + y, Cell.Glyph(glyph.ToString(), cell.Style.WithForeground(Color.FromRgb(32, 32, 32)), 1));
                }
            }
        }
    }
}
