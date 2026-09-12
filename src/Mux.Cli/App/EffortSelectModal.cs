namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// The reasoning-effort picker. It behaves like a <see cref="SelectModal"/> — Up/Down move, Enter
    /// chooses, Escape cancels — but sizes its own box so the (long) title and every row are shown without
    /// truncation on a normal-width terminal. Completes with the selected zero-based index, or null when
    /// cancelled.
    /// </summary>
    public sealed class EffortSelectModal : Modal
    {
        #region Private-Members

        // A comfortable minimum so the box is never cramped; Render still grows it to fit the title.
        private const int MinInnerWidth = 40;
        private const int MaxInnerWidth = 60;

        private readonly string _Title;
        private readonly ListView<string> _List = new ListView<string>();
        private Rect _ListRect;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="EffortSelectModal"/> class.
        /// </summary>
        /// <param name="title">The title. Must not be null.</param>
        /// <param name="options">The row labels. Must not be null or empty.</param>
        /// <param name="selectedIndex">The row to preselect; clamped into range.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="options"/> is empty.</exception>
        public EffortSelectModal(string title, IReadOnlyList<string> options, int selectedIndex)
        {
            _Title = title ?? throw new ArgumentNullException(nameof(title));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Count == 0) throw new ArgumentException("At least one option is required.", nameof(options));

            List<string> copy = new List<string>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                copy.Add(options[i]);
            }

            _List.SetItems(copy);
            for (int i = 0; i < Math.Clamp(selectedIndex, 0, options.Count - 1); i++)
            {
                _List.HandleKey(KeyEvent.Special(KeyCode.Down));
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                RequestClose(null);
                return true;
            }

            if (key.Code == KeyCode.Enter)
            {
                Close(_List.SelectedIndex);
                return true;
            }

            _List.HandleKey(key);
            return true;
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null || _ListRect.Width <= 0)
            {
                return true;
            }

            MouseEvent local = new MouseEvent(mouse.Kind, mouse.Button, mouse.X - _ListRect.X, mouse.Y - _ListRect.Y, mouse.Modifiers, mouse.ClickCount);

            if (mouse.Kind == MouseEventKind.Wheel)
            {
                _List.HandleMouse(local);
                return true;
            }

            if (mouse.Kind == MouseEventKind.Press
                && mouse.Button == MouseButton.Left
                && mouse.X >= _ListRect.X && mouse.X < _ListRect.X + _ListRect.Width
                && mouse.Y >= _ListRect.Y && mouse.Y < _ListRect.Y + _ListRect.Height
                && _List.HandleMouse(local))
            {
                Close(_List.SelectedIndex);
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            Padding pad = ContentPadding;
            int hintRows = 2; // blank separator + hint line

            // Grow the inner width to hold the longest row and the title so neither is truncated.
            int longest = _Title.Length;
            for (int i = 0; i < _List.Items.Count; i++)
            {
                if (_List.Items[i].Length > longest) longest = _List.Items[i].Length;
            }

            int desired = Math.Clamp(longest, MinInnerWidth, MaxInnerWidth);
            int innerWidth = Math.Min(desired, surface.Size.Width - 2 - pad.Horizontal);
            int listHeight = Math.Min(_List.Items.Count, surface.Size.Height - 2 - pad.Vertical - hintRows);
            if (innerWidth < 4 || listHeight < 1)
            {
                return;
            }

            int width = innerWidth + 2 + pad.Horizontal;
            int height = listHeight + hintRows + 2 + pad.Vertical;
            int x = (surface.Size.Width - width) / 2;
            int y = (surface.Size.Height - height) / 2;
            Rect box = new Rect(x, y, width, height);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            int contentX = x + 1 + pad.Left;
            int listTop = y + 1 + pad.Top;
            _ListRect = new Rect(contentX, listTop, innerWidth, listHeight);
            if (surface is BufferSurface buffer)
            {
                _List.Render(buffer.CreateView(_ListRect));
            }

            int hintRow = listTop + listHeight + 1;
            surface.DrawText(
                contentX,
                hintRow,
                Trim("↑↓ move · Enter apply · Esc cancel", innerWidth),
                CellStyle.Default.WithForeground(Color.FromPalette(8)));
        }

        #endregion

        #region Private-Methods

        private static string Trim(string text, int width)
        {
            if (width <= 0) return string.Empty;
            return text.Length <= width ? text : text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
