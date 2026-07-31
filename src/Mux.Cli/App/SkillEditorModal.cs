namespace Mux.Cli.App
{
    using System;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A near-full-screen modal that edits a skill's <c>SKILL.md</c> in place with a multi-line
    /// <see cref="TextEditor"/>. Ctrl+S completes with the edited text (the caller writes and re-validates it);
    /// Escape cancels, completing with null. Enter inserts a newline; the usual editing keys move, insert, and
    /// delete.
    /// </summary>
    public sealed class SkillEditorModal : Modal
    {
        #region Private-Members

        private const int PadX = 2;
        private const int PadY = 1;

        private readonly string _Title;
        private readonly TextEditor _Editor = new TextEditor { IsFocused = true };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillEditorModal"/> class.
        /// </summary>
        /// <param name="title">The box title (for example the skill name). Must not be null.</param>
        /// <param name="initialText">The initial file contents. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SkillEditorModal(string title, string initialText)
        {
            _Title = title ?? throw new ArgumentNullException(nameof(title));
            _Editor.Text = initialText ?? throw new ArgumentNullException(nameof(initialText));
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

            if (key.Code == KeyCode.Character
                && (key.Modifiers & KeyModifiers.Ctrl) != 0
                && char.ToLowerInvariant((char)key.Rune) == 's')
            {
                Close(_Editor.Text);
                return true;
            }

            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                _Editor.InsertNewline();
                return true;
            }

            _Editor.HandleKey(key);
            return true;
        }

        /// <inheritdoc/>
        public override void Render(ISurface surface)
        {
            if (surface == null) throw new ArgumentNullException(nameof(surface));

            int screenWidth = surface.Size.Width;
            int screenHeight = surface.Size.Height;

            int boxWidth = Math.Min(screenWidth, Math.Max(20, screenWidth - 4));
            int boxHeight = Math.Min(screenHeight, Math.Max(6, screenHeight - 2));
            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(6)), _Title);

            int contentX = boxX + 1 + PadX;
            int contentTop = boxY + 1 + PadY;
            int contentWidth = boxWidth - 2 - (2 * PadX);
            int hintRow = boxY + boxHeight - 2;
            int editorHeight = hintRow - contentTop - 1;
            if (contentWidth < 4 || editorHeight < 1)
            {
                return;
            }

            // The TextEditor renders into a BufferSurface, so render it into a buffer and copy the cells into
            // the modal box (the same pattern the prompt editor uses).
            CellBuffer buffer = new CellBuffer(contentWidth, editorHeight);
            _Editor.Render(new BufferSurface(buffer));
            for (int y = 0; y < editorHeight; y++)
            {
                for (int x = 0; x < contentWidth; x++)
                {
                    surface.Set(contentX + x, contentTop + y, buffer.Get(x, y));
                }
            }

            surface.DrawText(
                contentX,
                hintRow,
                Trim("Ctrl+S save · Esc cancel · Enter newline", contentWidth),
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
