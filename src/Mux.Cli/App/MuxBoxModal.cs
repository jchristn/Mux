namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>
    /// A modal that renders multi-line content verbatim (no reflow) in a centered bordered box, with an
    /// optional dimmed hint line at the bottom. Used for the startup splash, the help/keymap screen, and
    /// detail views where preserving line layout matters. Enter or Escape closes it (returning 0).
    /// </summary>
    public sealed class MuxBoxModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;

        private readonly string _Title;
        private readonly IReadOnlyList<string> _Lines;
        private readonly string _Hint;
        private readonly bool _Centered;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxBoxModal"/> class.
        /// </summary>
        /// <param name="title">The box title. May be empty.</param>
        /// <param name="lines">The content lines, rendered verbatim. Must not be null.</param>
        /// <param name="hint">An optional dimmed footer hint; empty to omit.</param>
        /// <param name="centered">When true, each content line and the hint are horizontally centered in the box.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null.</exception>
        public MuxBoxModal(string title, IReadOnlyList<string> lines, string hint = "Enter / Esc to close", bool centered = false)
        {
            _Title = title ?? string.Empty;
            _Lines = lines ?? throw new ArgumentNullException(nameof(lines));
            _Hint = hint ?? string.Empty;
            _Centered = centered;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            // Any key dismisses the box so it never blocks the user from getting to the prompt.
            Close(0);
            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Measure(_Title);
            for (int i = 0; i < _Lines.Count; i++)
            {
                contentWidth = Math.Max(contentWidth, Measure(_Lines[i]));
            }

            if (_Hint.Length > 0)
            {
                contentWidth = Math.Max(contentWidth, Measure(_Hint));
            }

            contentWidth = Math.Max(4, Math.Min(contentWidth, screenWidth - 2 - (2 * PadX)));

            int hintRows = _Hint.Length > 0 ? 2 : 0; // blank gap + hint
            int contentHeight = _Lines.Count + hintRows;
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));  // border + interior padding
            int boxHeight = Math.Min(screenHeight, contentHeight + 2 + (2 * PadY));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            int contentX = boxX + 1 + PadX;
            int firstRow = boxY + 1 + PadY;
            int lastContentRow = boxY + boxHeight - 2 - PadY;

            for (int i = 0; i < _Lines.Count; i++)
            {
                int row = firstRow + i;
                if (row > lastContentRow)
                {
                    break;
                }

                surface.DrawText(LineX(contentX, contentWidth, _Lines[i]), row, _Lines[i], CellStyle.Default);
            }

            if (_Hint.Length > 0)
            {
                int hintRow = lastContentRow;
                if (hintRow > firstRow + _Lines.Count - 1)
                {
                    surface.DrawText(LineX(contentX, contentWidth, _Hint), hintRow, _Hint, CellStyle.Default.WithForeground(Color.FromPalette(8)));
                }
            }
        }

        private int LineX(int contentX, int contentWidth, string line)
        {
            if (!_Centered)
            {
                return contentX;
            }

            return contentX + Math.Max(0, (contentWidth - Measure(line)) / 2);
        }

        #endregion

        #region Private-Methods

        private static int Measure(string text)
        {
            return TUIKit.Unicode.Graphemes.MeasureWidth(text ?? string.Empty);
        }

        #endregion
    }
}
