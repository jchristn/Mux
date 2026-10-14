namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Unicode;
    using TUIKit.Widgets;

    /// <summary>
    /// A near-full-screen modal that shows a Markdown document through TUIKit's width-aware renderer (a
    /// <see cref="MarkdownView"/>), so tables, lists, quotes, and code lay out at the box's width and reflow on
    /// resize. Up/Down, PageUp/PageDown, Home/End, and the mouse wheel scroll; Escape or Enter closes it.
    /// </summary>
    public sealed class MarkdownBoxModal : Modal
    {
        #region Private-Members

        private const int PadX = 2;
        private const int PadY = 1;
        private const int MaxContentWidth = 110;

        private readonly string _Title;
        private readonly MarkdownView _View;
        private int _PageRows = 1;
        private Rect _ViewRect;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MarkdownBoxModal"/> class.
        /// </summary>
        /// <param name="title">The box title. May be empty.</param>
        /// <param name="markdown">The Markdown source. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="markdown"/> is null.</exception>
        public MarkdownBoxModal(string title, string markdown)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            _Title = title ?? string.Empty;
            _View = new MarkdownView(markdown, MarkdownOptions.Create());
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Gets the first rendered row shown at the top of the box.
        /// </summary>
        public int ScrollOffset => _View.ScrollOffset;

        /// <summary>
        /// Gets the rendered rows at a width, for tests and measurement.
        /// </summary>
        /// <param name="width">The width in columns. Must be 1 or greater.</param>
        /// <returns>The rendered rows. Never null.</returns>
        public IReadOnlyList<StyledText> RowsAt(int width) => _View.RowsAt(width);

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Escape:
                case KeyCode.Enter:
                    Close(0);
                    return true;
                case KeyCode.Up:
                    ScrollBy(-1);
                    return true;
                case KeyCode.Down:
                    ScrollBy(1);
                    return true;
                case KeyCode.PageUp:
                    ScrollBy(-_PageRows);
                    return true;
                case KeyCode.PageDown:
                    ScrollBy(_PageRows);
                    return true;
                case KeyCode.Home:
                    _View.ScrollOffset = 0;
                    return true;
                case KeyCode.End:
                    _View.ScrollOffset = Math.Max(0, _View.ContentHeight - _PageRows);
                    return true;
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse != null && (mouse.Button == MouseButton.WheelUp || mouse.Button == MouseButton.WheelDown))
            {
                _View.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - _ViewRect.X, mouse.Y - _ViewRect.Y, mouse.Modifiers, mouse.ClickCount));
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Math.Max(4, Math.Min(MaxContentWidth, screenWidth - 2 - (2 * PadX)));
            int hintRows = 2; // blank gap + hint
            int maxBodyRows = Math.Max(1, screenHeight - 2 - (2 * PadY) - hintRows);
            int bodyRows = Math.Max(1, Math.Min(maxBodyRows, _View.RowsAt(contentWidth).Count));

            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));
            int boxHeight = Math.Min(screenHeight, bodyRows + hintRows + 2 + (2 * PadY));
            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            _PageRows = bodyRows;
            _ViewRect = new Rect(boxX + 1 + PadX, boxY + 1 + PadY, contentWidth, bodyRows);
            _View.Render(new SurfaceView(surface, _ViewRect));

            int total = _View.ContentHeight;
            string hint = total > bodyRows
                ? $"↑↓ PgUp PgDn scroll · {Math.Min(total, _View.ScrollOffset + bodyRows)}/{total} · Enter / Esc to close"
                : "Enter / Esc to close";
            int hintRow = boxY + boxHeight - 1 - PadY;
            if (hintRow > _ViewRect.Y + bodyRows - 1)
            {
                surface.DrawText(_ViewRect.X, hintRow, TextFit.Truncate(hint, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        #endregion

        #region Private-Methods

        private void ScrollBy(int delta)
        {
            int max = Math.Max(0, _View.ContentHeight - _PageRows);
            _View.ScrollOffset = Math.Max(0, Math.Min(max, _View.ScrollOffset + delta));
        }

        #endregion
    }
}
