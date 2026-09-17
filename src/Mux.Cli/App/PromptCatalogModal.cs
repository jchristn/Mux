namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal for browsing and editing the operational prompt catalog — every model-facing prompt that is not
    /// a switchable persona (compaction, title generation, tool descriptions, tool-result payloads, section
    /// lead-ins, diagnostics). Entries are listed grouped by kind; a global-scoped entry can be edited (its
    /// override is validated for required placeholders and persisted to the <c>operational</c> map of
    /// <c>prompts.json</c>) or reset to its coded default. Profile-scoped persona rows (system, tools-disabled)
    /// are shown read-only because they are edited in the profile editor. All changes are applied in-process
    /// through <see cref="PromptResolver"/> as they are made, so <see cref="Modal.Completion"/> yields null.
    /// </summary>
    public sealed class PromptCatalogModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;

        private readonly List<PromptDefinition> _Entries;
        private readonly TextEditor _Editor = new TextEditor();

        private PromptResolver _Resolver;
        private int _Selected;
        private int _ScrollTop;
        private bool _Editing;
        private string _Status = string.Empty;

        // Click hit-testing geometry captured on the last render.
        private readonly List<Hit> _RowHits = new List<Hit>();
        private Rect _EditorRect;

        private sealed class Hit
        {
            public Hit(int y, int index)
            {
                Y = y;
                Index = index;
            }

            public int Y { get; }

            public int Index { get; }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PromptCatalogModal"/> class over the coded catalog.
        /// </summary>
        public PromptCatalogModal()
        {
            _Entries = new List<PromptDefinition>(PromptCatalog.All);
            _Entries.Sort(CompareEntries);
            _Resolver = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
            _Selected = 0;
            LoadSelectedIntoEditor();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && key.Rune == 0)
            {
                return true;
            }

            if (_Editing)
            {
                return HandleEditKey(key);
            }

            return HandleNavKey(key);
        }

        /// <inheritdoc/>
        public override bool HandleMouse(MouseEvent mouse)
        {
            if (mouse == null || mouse.Kind != MouseEventKind.Press || mouse.Button != MouseButton.Left || _Editing)
            {
                return true;
            }

            foreach (Hit hit in _RowHits)
            {
                if (mouse.Y == hit.Y)
                {
                    _Selected = hit.Index;
                    _Status = string.Empty;
                    EnsureVisible();
                    LoadSelectedIntoEditor();
                    return true;
                }
            }

            if (_EditorRect.Width > 0
                && mouse.X >= _EditorRect.X && mouse.X < _EditorRect.X + _EditorRect.Width
                && mouse.Y >= _EditorRect.Y && mouse.Y < _EditorRect.Y + _EditorRect.Height)
            {
                BeginEdit();
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

            int boxWidth = Math.Min(screenWidth, Math.Min(screenWidth - 4, 150));
            if (boxWidth < 20) boxWidth = Math.Min(screenWidth, 20);
            int boxHeight = Math.Min(screenHeight, Math.Max(10, screenHeight - 2));
            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), "Operational Prompts");

            int contentX = boxX + 1 + PadX;
            int contentWidth = Math.Max(1, boxWidth - 2 - (2 * PadX));
            int top = boxY + 1 + PadY;
            int bottomHintRow = boxY + boxHeight - 2;

            PromptDefinition? selected = (_Selected >= 0 && _Selected < _Entries.Count) ? _Entries[_Selected] : null;

            string hint;
            if (_Editing)
            {
                hint = "editing — Esc save · Enter newline";
            }
            else if (selected != null && selected.Scope == PromptScope.Global)
            {
                hint = "↑↓ select · e/Enter edit · r reset · Esc close";
            }
            else
            {
                hint = "↑↓ select · (persona rows are edited in the profile editor) · Esc close";
            }

            IReadOnlyList<string> hintLines = HintText.Wrap(hint, contentWidth);
            int firstHintRow = bottomHintRow - hintLines.Count + 1;

            // A status/error line sits just above the footer hint.
            int statusRow = firstHintRow - 1;
            if (!string.IsNullOrEmpty(_Status) && statusRow > top)
            {
                surface.DrawText(contentX, statusRow, Trim(_Status, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(11)));
            }

            int listWidth = Math.Max(16, Math.Min(46, contentWidth / 2));
            int detailX = contentX + listWidth + 2;
            int detailWidth = Math.Max(1, contentWidth - listWidth - 2);
            int listBottom = statusRow - 1;
            int listHeight = Math.Max(1, listBottom - top);

            RenderList(surface, contentX, top, listWidth, listHeight);
            RenderDetail(surface, detailX, top, detailWidth, listHeight, selected);

            for (int i = 0; i < hintLines.Count; i++)
            {
                surface.DrawText(contentX, firstHintRow + i, Trim(hintLines[i], contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
            }
        }

        #endregion

        #region Private-Methods

        private bool HandleNavKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Escape:
                    Close(null);
                    return true;
                case KeyCode.Up:
                case KeyCode.Left:
                    Move(-1);
                    return true;
                case KeyCode.Down:
                case KeyCode.Right:
                    Move(1);
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
                    case 'r': ResetSelected(); return true;
                }
            }

            return true;
        }

        private bool HandleEditKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                CommitEdit();
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

        private void Move(int delta)
        {
            if (_Entries.Count == 0)
            {
                return;
            }

            int count = _Entries.Count;
            _Selected = ((_Selected + delta) % count + count) % count;
            _Status = string.Empty;
            EnsureVisible();
            LoadSelectedIntoEditor();
        }

        private void EnsureVisible()
        {
            // The visible window is recomputed at render time; keep a conservative scroll so the selection is
            // never above the window, and let the render clamp the bottom.
            if (_Selected < _ScrollTop)
            {
                _ScrollTop = _Selected;
            }
        }

        private void BeginEdit()
        {
            if (_Selected < 0 || _Selected >= _Entries.Count)
            {
                return;
            }

            if (_Entries[_Selected].Scope != PromptScope.Global)
            {
                _Status = "This prompt is edited in the profile editor.";
                return;
            }

            _Status = string.Empty;
            _Editing = true;
        }

        private void CommitEdit()
        {
            _Editing = false;
            if (_Selected < 0 || _Selected >= _Entries.Count)
            {
                return;
            }

            PromptDefinition entry = _Entries[_Selected];
            try
            {
                PromptResolver.SetOverride(entry.Key, _Editor.Text);
                _Resolver = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
                _Status = "Saved override for " + entry.DisplayName + ".";
            }
            catch (ArgumentException exception)
            {
                // Validation failed (a required placeholder was dropped). Keep the edited text so the user can
                // fix it, and surface the reason.
                _Status = exception.Message;
                _Editing = true;
            }
        }

        private void ResetSelected()
        {
            if (_Selected < 0 || _Selected >= _Entries.Count)
            {
                return;
            }

            PromptDefinition entry = _Entries[_Selected];
            if (entry.Scope != PromptScope.Global || !_Resolver.IsOverridden(entry.Key))
            {
                return;
            }

            PromptResolver.ResetToDefault(entry.Key);
            _Resolver = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
            LoadSelectedIntoEditor();
            _Status = "Reset " + entry.DisplayName + " to its default.";
        }

        private void LoadSelectedIntoEditor()
        {
            if (_Selected < 0 || _Selected >= _Entries.Count)
            {
                _Editor.Text = string.Empty;
                return;
            }

            _Editor.Text = _Resolver.GetEffective(_Entries[_Selected].Key);
        }

        private void RenderList(ISurface surface, int x, int top, int width, int height)
        {
            _RowHits.Clear();

            if (_Selected >= _ScrollTop + height)
            {
                _ScrollTop = _Selected - height + 1;
            }
            if (_ScrollTop < 0)
            {
                _ScrollTop = 0;
            }

            for (int row = 0; row < height; row++)
            {
                int index = _ScrollTop + row;
                if (index >= _Entries.Count)
                {
                    break;
                }

                PromptDefinition entry = _Entries[index];
                bool selected = index == _Selected;
                bool overridden = entry.Scope == PromptScope.Global && _Resolver.IsOverridden(entry.Key);
                string marker = overridden ? "● " : "  ";
                string text = marker + entry.DisplayName;

                CellStyle style = selected
                    ? CellStyle.Default.WithForeground(Color.FromPalette(6)).WithAttribute(CellAttributes.Reverse, true)
                    : CellStyle.Default.WithForeground(entry.Scope == PromptScope.Global ? Color.FromPalette(7) : Color.FromPalette(8));

                int y = top + row;
                surface.DrawText(x, y, Trim(text, width), style);
                _RowHits.Add(new Hit(y, index));
            }
        }

        private void RenderDetail(ISurface surface, int x, int top, int width, int height, PromptDefinition? entry)
        {
            if (entry == null || width <= 0 || height <= 0)
            {
                _EditorRect = new Rect(0, 0, 0, 0);
                return;
            }

            CellStyle dim = CellStyle.Default.WithForeground(Color.FromPalette(8));
            CellStyle head = CellStyle.Default.WithForeground(Color.FromPalette(11)).WithAttribute(CellAttributes.Bold, true);

            int row = top;
            surface.DrawText(x, row, Trim(entry.DisplayName, width), head);
            row++;

            bool overridden = entry.Scope == PromptScope.Global && _Resolver.IsOverridden(entry.Key);
            string sub = entry.Key + " · " + entry.Kind.ToWireString() + " · " + (overridden ? "custom" : "default");
            surface.DrawText(x, row, Trim(sub, width), dim);
            row++;

            foreach (string line in HintText.Wrap(entry.Description ?? string.Empty, width))
            {
                if (row >= top + height - 1) break;
                surface.DrawText(x, row, Trim(line, width), dim);
                row++;
            }

            if (entry.Placeholders.Count > 0 && row < top + height - 1)
            {
                surface.DrawText(x, row, Trim("Keep: " + string.Join(", ", entry.Placeholders), width), dim);
                row++;
            }

            if (entry.Scope != PromptScope.Global && row < top + height - 1)
            {
                surface.DrawText(x, row, Trim("(edited in the profile editor)", width), dim);
                row++;
            }

            row++;
            int editTop = row;
            int editHeight = Math.Max(1, top + height - editTop);
            _EditorRect = new Rect(x, editTop, width, editHeight);
            _Editor.IsFocused = _Editing;
            CellBuffer buffer = new CellBuffer(width, editHeight);
            _Editor.Render(new BufferSurface(buffer));
            for (int yy = 0; yy < editHeight; yy++)
            {
                for (int xx = 0; xx < width; xx++)
                {
                    surface.Set(x + xx, editTop + yy, buffer.Get(xx, yy));
                }
            }
        }

        private static int CompareEntries(PromptDefinition a, PromptDefinition b)
        {
            int byKind = ((int)a.Kind).CompareTo((int)b.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        }

        private static string Trim(string text, int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            return text.Length <= width ? text : text.Substring(0, Math.Max(0, width - 1)) + "…";
        }

        #endregion
    }
}
