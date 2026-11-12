namespace Mux.Desktop.Controls
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Media;
    using Mux.Core.Branding;

    /// <summary>
    /// Draws block-character art (the mux wordmark by default) as filled rectangles instead of text, so it is solid
    /// with any font: as a <see cref="TextBlock"/> the block glyphs leave gaps between rows (and sometimes columns)
    /// because they are shorter than the font's line height. Each half-cell is a square of <see cref="PixelSize"/>
    /// whole pixels, so edges fall on pixel boundaries and touching blocks show no seams. Shared by the desktop app
    /// and the tray agent (linked into Mux.Agent).
    /// </summary>
    public sealed class BlockArtView : Control
    {
        #region Public-Members

        /// <summary>The art, one string per row. Defaults to the mux wordmark.</summary>
        public IReadOnlyList<string> Lines
        {
            get => _Lines;
            set
            {
                _Lines = value ?? BlockArt.Wordmark;
                _Rectangles = BlockArt.Rectangles(_Lines);
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        /// <summary>The fill. Defaults to the mux green.</summary>
        public IBrush Fill
        {
            get => _Fill;
            set
            {
                _Fill = value ?? Brushes.Green;
                InvalidateVisual();
            }
        }

        /// <summary>The side of one half-cell square, in whole pixels (1 to 64). Defaults to 6.</summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the value is outside 1 to 64.</exception>
        public int PixelSize
        {
            get => _PixelSize;
            set
            {
                if (value < 1 || value > 64) throw new ArgumentOutOfRangeException(nameof(value), "PixelSize must be 1 to 64.");
                _PixelSize = value;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        #endregion

        #region Private-Members

        private IReadOnlyList<string> _Lines = BlockArt.Wordmark;
        private IReadOnlyList<(int X, int Y, int Width, int Height)> _Rectangles = BlockArt.Rectangles(BlockArt.Wordmark);
        private IBrush _Fill = new SolidColorBrush(Color.Parse("#3fb950"));
        private int _PixelSize = 6;

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            foreach ((int x, int y, int width, int height) in _Rectangles)
            {
                context.FillRectangle(_Fill, new Rect(x * _PixelSize, y * _PixelSize, width * _PixelSize, height * _PixelSize));
            }
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize)
        {
            (int columns, int halfRows) = BlockArt.Size(_Lines);
            return new Size(columns * _PixelSize, halfRows * _PixelSize);
        }

        #endregion
    }
}
