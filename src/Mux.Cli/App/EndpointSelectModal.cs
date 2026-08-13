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
        Remove
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
        public override void Render(ISurface surface)
        {
            if (surface == null)
                throw new ArgumentNullException(nameof(surface));

            Padding pad = ContentPadding;
            int hintRows = 2; // blank separator + hint line
            int innerWidth = Math.Min(56, surface.Size.Width - 2 - pad.Horizontal);
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
            if (surface is BufferSurface buffer)
                _List.Render(buffer.CreateView(new Rect(contentX, listTop, innerWidth, listHeight)));

            int hintRow = listTop + listHeight + 1;
            surface.DrawText(
                contentX,
                hintRow,
                Trim("↑↓ move · Enter switch · e edit · d/Del remove", innerWidth),
                CellStyle.Default.WithForeground(Color.FromPalette(8)));
        }

        #endregion

        #region Private-Methods

        private bool IsEndpointRow(int index)
        {
            return index >= 0 && index < _EndpointCount;
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
