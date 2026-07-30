namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal for editing the pending-prompt queue while processing is paused. Lists the queued prompts
    /// and lets the user edit any prompt's text, remove entries, and reorder them. Enter/Escape (when not
    /// editing a row) close the modal; <see cref="Modal.Completion"/> yields the edited list of prompts in
    /// their new order, which the shell writes back as the queue.
    /// </summary>
    public sealed class QueueEditorModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 60;

        private readonly List<string> _Items;
        private readonly TextField _Editor = new TextField();
        private int _Selected;
        private bool _Editing;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="QueueEditorModal"/> class.
        /// </summary>
        /// <param name="items">The queued prompts to edit. A copy is taken; the original is not mutated.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is null.</exception>
        public QueueEditorModal(IReadOnlyList<string> items)
        {
            if (items is null) throw new ArgumentNullException(nameof(items));
            _Items = new List<string>(items);
            _Selected = _Items.Count > 0 ? 0 : -1;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (_Editing)
            {
                return HandleEditKey(key);
            }

            switch (key.Code)
            {
                case KeyCode.Escape:
                case KeyCode.Enter when key.Modifiers == KeyModifiers.None && _Items.Count == 0:
                    Close(new List<string>(_Items));
                    return true;
                case KeyCode.Up:
                    Move(-1);
                    return true;
                case KeyCode.Down:
                    Move(1);
                    return true;
                case KeyCode.Delete:
                    RemoveSelected();
                    return true;
                case KeyCode.Enter:
                    BeginEdit();
                    return true;
            }

            if (key.Code == KeyCode.Character)
            {
                switch (char.ToLowerInvariant((char)key.Rune))
                {
                    case 'e': BeginEdit(); return true;
                    case 'd': RemoveSelected(); return true;
                    case '[': Reorder(-1); return true;
                    case ']': Reorder(1); return true;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int contentWidth = Math.Max(8, Math.Min(ContentWidth, screenWidth - 2 - (2 * PadX)));
            int listRows = Math.Max(1, _Items.Count);
            int bodyRows = 1 /* hint */ + 1 /* blank */ + listRows;
            int boxHeight = Math.Min(screenHeight, bodyRows + 2 + (2 * PadY));
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), "Edit queue");

            int contentX = boxX + 1 + PadX;
            int row = boxY + 1 + PadY;

            surface.DrawText(contentX, row, Trim("↑↓ move · e edit · d delete · [ ] reorder · Esc done", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
            row += 2;

            if (_Items.Count == 0)
            {
                surface.DrawText(contentX, row, Trim("(queue is empty)", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
                return;
            }

            for (int i = 0; i < _Items.Count; i++)
            {
                int y = row + i;
                if (y >= boxY + boxHeight - 1 - PadY)
                {
                    break;
                }

                bool selected = i == _Selected;
                string marker = selected ? "▶ " : "  ";
                string body = _Editing && selected ? _Editor.Value + "▏" : _Items[i];
                string line = marker + (i + 1) + ". " + body;

                CellStyle style = selected
                    ? CellStyle.Default.WithForeground(Color.FromPalette((byte)(_Editing ? 11 : 6)))
                    : CellStyle.Default.WithForeground(Color.FromPalette(7));
                surface.DrawText(contentX, y, Trim(line, contentWidth), style);
            }
        }

        #endregion

        #region Private-Methods

        private bool HandleEditKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                string edited = _Editor.Value.Trim();
                if (edited.Length > 0)
                {
                    _Items[_Selected] = edited;
                }

                _Editing = false;
                return true;
            }

            if (key.Code == KeyCode.Escape)
            {
                _Editing = false;
                return true;
            }

            _Editor.HandleKey(key);
            return true;
        }

        private void Move(int delta)
        {
            if (_Items.Count == 0)
            {
                return;
            }

            _Selected = Math.Clamp(_Selected + delta, 0, _Items.Count - 1);
        }

        private void BeginEdit()
        {
            if (_Selected < 0 || _Selected >= _Items.Count)
            {
                return;
            }

            _Editor.Value = _Items[_Selected];
            _Editing = true;
        }

        private void RemoveSelected()
        {
            if (_Selected < 0 || _Selected >= _Items.Count)
            {
                return;
            }

            _Items.RemoveAt(_Selected);
            if (_Selected >= _Items.Count)
            {
                _Selected = _Items.Count - 1;
            }
        }

        private void Reorder(int delta)
        {
            int target = _Selected + delta;
            if (_Selected < 0 || target < 0 || target >= _Items.Count)
            {
                return;
            }

            (_Items[_Selected], _Items[target]) = (_Items[target], _Items[_Selected]);
            _Selected = target;
        }

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
