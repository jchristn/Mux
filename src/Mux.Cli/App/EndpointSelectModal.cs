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
    /// How the endpoints list was dismissed for the highlighted row.
    /// </summary>
    public enum EndpointModalActivationEnum
    {
        /// <summary>
        /// The default activation (Enter): act on the highlighted row per its menu action.
        /// </summary>
        Select,

        /// <summary>
        /// The edit shortcut (e): edit the highlighted endpoint directly.
        /// </summary>
        Edit,

        /// <summary>
        /// The remove shortcut (d or Delete): remove the highlighted endpoint directly.
        /// </summary>
        Remove,

        /// <summary>
        /// The validate shortcut (v): probe the highlighted endpoint and show the result.
        /// </summary>
        Validate
    }

    /// <summary>
    /// The result of the endpoints list modal: the highlighted row index and how it was activated.
    /// </summary>
    public sealed class EndpointModalResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointModalResult"/> class.
        /// </summary>
        /// <param name="index">The zero-based highlighted row index.</param>
        /// <param name="activation">How the row was activated.</param>
        public EndpointModalResult(int index, EndpointModalActivationEnum activation)
        {
            Index = index;
            Activation = activation;
        }

        /// <summary>
        /// Gets the zero-based index of the highlighted row.
        /// </summary>
        public int Index { get; }

        /// <summary>
        /// Gets how the row was activated.
        /// </summary>
        public EndpointModalActivationEnum Activation { get; }
    }

    /// <summary>
    /// The endpoints / models picker. It behaves like a <see cref="SelectModal"/> — Up/Down move, Enter
    /// chooses, Escape cancels — and additionally offers per-row shortcuts while an endpoint is highlighted:
    /// <c>e</c> edits it and <c>d</c> or Delete removes it. The shortcuts fire only on the endpoint rows
    /// (the first <c>endpointCount</c> rows); they are ignored on the separator and management rows below.
    /// Completes with an <see cref="EndpointModalResult"/>, or null when cancelled.
    /// </summary>
    public sealed class EndpointSelectModal : Modal
    {
        #region Private-Members

        private readonly string _Title;
        private readonly ListView<string> _List = new ListView<string>();
        private readonly int _EndpointCount;

        // Geometry captured on the last Render so HandleMouse can hit-test clicks against the rows and the
        // key-hint line (a modal draws its own box, so it owns the coordinate mapping).
        private Rect _ListRect;
        private int _HintRow = -1;
        private readonly List<HintZone> _HintZones = new List<HintZone>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="EndpointSelectModal"/> class.
        /// </summary>
        /// <param name="title">The title. Must not be null.</param>
        /// <param name="options">The row labels. Must not be null or empty.</param>
        /// <param name="endpointCount">The number of leading rows that are endpoints eligible for the edit
        /// and remove shortcuts.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="options"/> is empty.</exception>
        public EndpointSelectModal(string title, IReadOnlyList<string> options, int endpointCount)
        {
            _Title = title ?? throw new ArgumentNullException(nameof(title));
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (options.Count == 0)
                throw new ArgumentException("At least one option is required.", nameof(options));

            _EndpointCount = Math.Max(0, endpointCount);

            List<string> copy = new List<string>(options.Count);
            for (int i = 0; i < options.Count; i++)
                copy.Add(options[i]);

            _List.SetItems(copy);
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
                Close(new EndpointModalResult(_List.SelectedIndex, EndpointModalActivationEnum.Select));
                return true;
            }

            // The Delete key removes the highlighted endpoint.
            if (key.Code == KeyCode.Delete && IsEndpointRow(_List.SelectedIndex))
            {
                Close(new EndpointModalResult(_List.SelectedIndex, EndpointModalActivationEnum.Remove));
                return true;
            }

            // The letter shortcuts fire only as plain keystrokes so Ctrl/Alt chords are left alone.
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && IsEndpointRow(_List.SelectedIndex))
            {
                if (key.Rune == 'e' || key.Rune == 'E')
                {
                    Close(new EndpointModalResult(_List.SelectedIndex, EndpointModalActivationEnum.Edit));
                    return true;
                }

                if (key.Rune == 'v' || key.Rune == 'V')
                {
                    Close(new EndpointModalResult(_List.SelectedIndex, EndpointModalActivationEnum.Validate));
                    return true;
                }

                if (key.Rune == 'd' || key.Rune == 'D')
                {
                    Close(new EndpointModalResult(_List.SelectedIndex, EndpointModalActivationEnum.Remove));
                    return true;
                }
            }

            _List.HandleKey(key);
            return true;
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null)
            {
                return true;
            }

            // Wheel scrolls the list.
            if (mouse.Kind == MouseEventKind.Wheel && _ListRect.Width > 0)
            {
                _List.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - _ListRect.X, mouse.Y - _ListRect.Y, mouse.Modifiers, mouse.ClickCount));
                return true;
            }

            if (mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left)
            {
                return true;
            }

            // A click on a row selects it (the list maps the y to an index, accounting for scroll) and, when
            // it landed on a real row, activates it — the same as pressing Enter, so "+ Add endpoint…" runs.
            if (_ListRect.Width > 0
                && mouse.X >= _ListRect.X && mouse.X < _ListRect.X + _ListRect.Width
                && mouse.Y >= _ListRect.Y && mouse.Y < _ListRect.Y + _ListRect.Height)
            {
                bool hitRow = _List.HandleMouse(new MouseEvent(mouse.Kind, mouse.Button, mouse.X - _ListRect.X, mouse.Y - _ListRect.Y, mouse.Modifiers, mouse.ClickCount));
                if (hitRow)
                {
                    Close(new EndpointModalResult(_List.SelectedIndex, EndpointModalActivationEnum.Select));
                }

                return true;
            }

            // A click on a key-hint segment fires that action against the highlighted endpoint.
            if (mouse.Y == _HintRow)
            {
                foreach (HintZone zone in _HintZones)
                {
                    if (mouse.X >= zone.Start && mouse.X < zone.End)
                    {
                        if (zone.Activation == EndpointModalActivationEnum.Select || IsEndpointRow(_List.SelectedIndex))
                        {
                            Close(new EndpointModalResult(_List.SelectedIndex, zone.Activation));
                        }

                        return true;
                    }
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));

            Padding pad = ContentPadding;
            int hintRows = 2; // blank separator + hint line
            int innerWidth = Math.Min(64, surface.Size.Width - 2 - pad.Horizontal);
            int listHeight = Math.Min(_List.Items.Count, surface.Size.Height - 2 - pad.Vertical - hintRows);
            if (innerWidth < 4 || listHeight < 1)
                return;

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
                _List.Render(buffer.CreateView(_ListRect));

            int hintRow = listTop + listHeight + 1;
            _HintRow = hintRow;
            surface.DrawText(
                contentX,
                hintRow,
                Trim(BuildHint(contentX, innerWidth), innerWidth),
                CellStyle.Default.WithForeground(Color.FromPalette(8)));
        }

        // Builds the key-hint line and, as a side effect, records the clickable X range of each action
        // segment (so a click on "e edit" or "v validate" fires that action, matching the keyboard shortcut).
        private string BuildHint(int contentX, int innerWidth)
        {
            _HintZones.Clear();
            Segment[] segments =
            {
                new Segment("↑↓ move", null),
                new Segment("Enter switch", EndpointModalActivationEnum.Select),
                new Segment("e edit", EndpointModalActivationEnum.Edit),
                new Segment("v validate", EndpointModalActivationEnum.Validate),
                new Segment("d/Del remove", EndpointModalActivationEnum.Remove),
            };

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            const string separator = " · ";
            int column = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(separator);
                    column += separator.Length;
                }

                int start = contentX + column;
                builder.Append(segments[i].Text);
                column += segments[i].Text.Length;

                // Only record zones that fall within the rendered (possibly trimmed) width.
                EndpointModalActivationEnum? activation = segments[i].Activation;
                if (activation.HasValue && start < contentX + innerWidth)
                {
                    _HintZones.Add(new HintZone(start, contentX + column, activation.Value));
                }
            }

            return builder.ToString();
        }

        #endregion

        #region Private-Methods

        private bool IsEndpointRow(int index)
        {
            return index >= 0 && index < _EndpointCount;
        }

        // A key-hint segment: its display text and, when clickable, the action it fires.
        private sealed class Segment
        {
            public Segment(string text, EndpointModalActivationEnum? activation)
            {
                Text = text;
                Activation = activation;
            }

            public string Text { get; }

            public EndpointModalActivationEnum? Activation { get; }
        }

        // The rendered X range [Start, End) of a clickable hint segment and the action it fires.
        private sealed class HintZone
        {
            public HintZone(int start, int end, EndpointModalActivationEnum activation)
            {
                Start = start;
                End = end;
                Activation = activation;
            }

            public int Start { get; }

            public int End { get; }

            public EndpointModalActivationEnum Activation { get; }
        }

        private static string Trim(string text, int width)
        {
            if (width <= 0)
                return string.Empty;

            return text.Length <= width ? text : text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
