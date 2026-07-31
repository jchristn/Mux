namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>
    /// A modal presenting a scrollable list of checkboxes for choosing several items at once. Up/Down
    /// move the cursor (the view scrolls to keep it visible), Space toggles the highlighted item, "a"
    /// toggles every item, Enter confirms (completing with the <see cref="List{Int32}"/> of checked
    /// zero-based indices), and Escape cancels (completing with null). An optional hint line is drawn
    /// above the list.
    /// </summary>
    public sealed class MultiSelectModal : Modal
    {
        #region Private-Members

        private const int PadX = 2;
        private const int PadY = 1;
        private const int MaxContentWidth = 64;

        private readonly string _Title;
        private readonly string? _Hint;
        private readonly List<string> _Items;
        private readonly bool[] _Checked;
        private int _Cursor;
        private int _Top;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiSelectModal"/> class.
        /// </summary>
        /// <param name="title">The box title. Must not be null.</param>
        /// <param name="items">The selectable items. Must not be null or empty.</param>
        /// <param name="hint">An optional hint line drawn above the list, or null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="title"/> or <paramref name="items"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="items"/> is empty.</exception>
        public MultiSelectModal(string title, IReadOnlyList<string> items, string? hint = null)
        {
            _Title = title ?? throw new ArgumentNullException(nameof(title));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count == 0) throw new ArgumentException("At least one item is required.", nameof(items));

            _Items = new List<string>(items);
            _Checked = new bool[_Items.Count];
            _Hint = hint;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                Close(null);
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                List<int> selected = new List<int>();
                for (int i = 0; i < _Checked.Length; i++)
                {
                    if (_Checked[i]) selected.Add(i);
                }

                Close(selected);
                return true;
            }

            if (key.Code == KeyCode.Up)
            {
                if (_Cursor > 0) _Cursor--;
                return true;
            }

            if (key.Code == KeyCode.Down)
            {
                if (_Cursor < _Items.Count - 1) _Cursor++;
                return true;
            }

            if (key.Code == KeyCode.Character && key.Rune == ' ')
            {
                if (_Cursor >= 0 && _Cursor < _Checked.Length)
                {
                    _Checked[_Cursor] = !_Checked[_Cursor];
                }

                return true;
            }

            // "a" toggles all: if anything is unchecked, check everything; otherwise clear everything.
            if (key.Code == KeyCode.Character && (key.Rune == 'a' || key.Rune == 'A'))
            {
                bool anyUnchecked = false;
                for (int i = 0; i < _Checked.Length; i++)
                {
                    if (!_Checked[i]) { anyUnchecked = true; break; }
                }

                for (int i = 0; i < _Checked.Length; i++)
                {
                    _Checked[i] = anyUnchecked;
                }

                return true;
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Math.Max(10, Math.Min(MaxContentWidth, screenWidth - 2 - (2 * PadX)));
            int headerRows = _Hint == null ? 0 : 2; // hint line + blank spacer
            int footerRows = 2;                      // blank spacer + key hint

            // Prefer a box tall enough for every item, capped to the screen.
            int desiredInner = headerRows + _Items.Count + footerRows;
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int boxHeight = Math.Min(screenHeight, desiredInner + 2 + (2 * PadY));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            int contentX = boxX + 1 + PadX;
            int contentTop = boxY + 1 + PadY;
            int contentBottom = boxY + boxHeight - 1 - PadY; // last usable content row (inclusive)
            if (contentBottom < contentTop)
            {
                return;
            }

            int row = contentTop;
            if (_Hint != null)
            {
                surface.DrawText(contentX, row, Trim(_Hint, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(3)));
                row += 2;
            }

            int footerRow = contentBottom;
            int listBottom = footerRow - 2;           // leave a blank line above the footer
            int firstListRow = row;
            int visibleRows = Math.Max(1, listBottom - firstListRow + 1);

            // Scroll so the cursor stays inside the visible window.
            if (_Cursor < _Top) _Top = _Cursor;
            else if (_Cursor >= _Top + visibleRows) _Top = _Cursor - visibleRows + 1;

            for (int i = 0; i < visibleRows; i++)
            {
                int index = _Top + i;
                if (index >= _Items.Count) break;

                int y = firstListRow + i;
                bool selected = index == _Cursor;
                string mark = _Checked[index] ? "[x] " : "[ ] ";
                string text = Trim(mark + _Items[index], contentWidth);

                CellStyle style;
                if (selected)
                {
                    style = CellStyle.Default.WithForeground(Color.FromRgb(0, 0, 0)).WithBackground(Color.FromPalette(6));
                    surface.Fill(new Rect(contentX, y, contentWidth, 1), Cell.Blank(style));
                }
                else
                {
                    style = _Checked[index]
                        ? CellStyle.Default.WithForeground(Color.FromPalette(10))
                        : CellStyle.Default;
                }

                surface.DrawText(contentX, y, text, style);
            }

            surface.DrawText(
                contentX,
                footerRow,
                Trim("Space toggle · a all · Enter import · Esc cancel", contentWidth),
                CellStyle.Default.WithForeground(Color.FromPalette(8)));
        }

        #endregion

        #region Private-Methods

        private static string Trim(string text, int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            return text.Length <= width ? text : text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
