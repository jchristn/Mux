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
    /// A single-select list modal that behaves exactly like TUIKit's <c>SelectModal</c> — Up/Down move, Enter
    /// returns the zero-based index, Escape returns -1 — but takes a caller-controlled maximum content width
    /// instead of the fixed 46-column cap. This gives wide, column-aligned rows (for example the command
    /// menu's title / key-chord / slash-alias columns) room to render without truncation.
    /// </summary>
    public sealed class WideSelectModal : Modal
    {
        private readonly string _Title;
        private readonly ListView<string> _List = new ListView<string>();
        private readonly int _MaxContentWidth;

        /// <summary>
        /// Initializes a new instance of the <see cref="WideSelectModal"/> class.
        /// </summary>
        /// <param name="title">The box title. Must not be null.</param>
        /// <param name="options">The options. Must not be null or empty.</param>
        /// <param name="maxContentWidth">The maximum inner content width in columns (clamped to at least 4).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="options"/> is empty.</exception>
        public WideSelectModal(string title, IReadOnlyList<string> options, int maxContentWidth)
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
            _MaxContentWidth = Math.Max(4, maxContentWidth);
        }

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter)
            {
                Close(_List.SelectedIndex);
                return true;
            }

            if (key.Code == KeyCode.Escape)
            {
                RequestClose(-1);
                return true;
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
            int innerWidth = Math.Min(_MaxContentWidth, surface.Size.Width - 2 - pad.Horizontal);
            int innerHeight = Math.Min(_List.Items.Count, surface.Size.Height - 2 - pad.Vertical);
            if (innerWidth < 4 || innerHeight < 1)
                return;

            int width = innerWidth + 2 + pad.Horizontal;
            int height = innerHeight + 2 + pad.Vertical;
            int x = (surface.Size.Width - width) / 2;
            int y = (surface.Size.Height - height) / 2;
            Rect box = new Rect(x, y, width, height);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            if (surface is BufferSurface buffer)
                _List.Render(buffer.CreateView(new Rect(x + 1 + pad.Left, y + 1 + pad.Top, innerWidth, innerHeight)));
        }
    }
}
