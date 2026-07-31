namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal for viewing and editing mux's prompt profiles. Each profile carries three switchable prompts
    /// (the system prompt, the tools-disabled system prompt, and the compaction prompt); an empty field falls
    /// back to the built-in default. The modal lets the user pick a profile, pick a field, edit the field's
    /// multi-line text, mark a profile active, and add, rename, or remove profiles. The incoming profiles are
    /// deep-copied so the originals are never mutated; <see cref="Modal.Completion"/> yields the edited working
    /// list (a <c>List&lt;PromptProfile&gt;</c> with exactly one active profile), which the caller persists and
    /// applies.
    /// </summary>
    public sealed class PromptEditorModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;

        /// <summary>
        /// The three editable prompt fields carried by a profile.
        /// </summary>
        private enum PromptField
        {
            System,
            ToolsDisabled,
            Compaction
        }

        private readonly List<PromptProfile> _Profiles;
        private readonly TextEditor _Editor = new TextEditor { IsFocused = true };
        private readonly TextField _Name = new TextField();

        private int _SelectedProfile;
        private PromptField _Field = PromptField.System;
        private bool _Editing;
        private bool _Naming;

        // True while a rename is in flight (as opposed to an add), so committing the name targets the current
        // profile instead of appending a new one.
        private bool _NamingRename;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="PromptEditorModal"/> class.
        /// </summary>
        /// <param name="profiles">The prompt profiles to edit. Each is deep-copied; the originals are not
        /// mutated. When the list is empty, a single empty "Default" profile is created.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profiles"/> is null.</exception>
        public PromptEditorModal(IReadOnlyList<PromptProfile> profiles)
        {
            if (profiles is null) throw new ArgumentNullException(nameof(profiles));

            _Profiles = new List<PromptProfile>(profiles.Count);
            for (int i = 0; i < profiles.Count; i++)
            {
                PromptProfile source = profiles[i];
                _Profiles.Add(new PromptProfile
                {
                    Name = source.Name ?? string.Empty,
                    IsActive = source.IsActive,
                    SystemPrompt = source.SystemPrompt ?? string.Empty,
                    ToolsDisabledPrompt = source.ToolsDisabledPrompt ?? string.Empty,
                    CompactionPrompt = source.CompactionPrompt ?? string.Empty
                });
            }

            if (_Profiles.Count == 0)
            {
                _Profiles.Add(new PromptProfile { Name = "Default", IsActive = true });
            }

            _SelectedProfile = 0;
            LoadFieldIntoEditor();
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

            if (_Naming)
            {
                return HandleNamingKey(key);
            }

            if (_Editing)
            {
                return HandleEditKey(key);
            }

            return HandleNavKey(key);
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
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), "Prompts");

            int contentX = boxX + 1 + PadX;
            int contentWidth = Math.Max(1, boxWidth - 2 - (2 * PadX));
            int row = boxY + 1 + PadY;
            int bottomHintRow = boxY + boxHeight - 2;

            // Row 1: the profile list.
            RenderProfileList(surface, contentX, row, contentWidth);
            row++;

            // Row 2: the field tabs.
            RenderFieldTabs(surface, contentX, row, contentWidth);
            row++;

            // Row 3: the placeholder note.
            surface.DrawText(
                contentX,
                row,
                Trim("{WorkingDirectory} and {ToolDescriptions} are filled in automatically.", contentWidth),
                CellStyle.Default.WithForeground(Color.FromPalette(8)));
            row += 2;

            // The remaining rows host the editor (or the name entry when naming).
            int editTop = row;
            int editHeight = Math.Max(1, bottomHintRow - 1 - editTop);
            if (editHeight >= 1)
            {
                if (_Naming)
                {
                    RenderNaming(surface, contentX, editTop, contentWidth, editHeight);
                }
                else
                {
                    RenderEditor(surface, contentX, editTop, contentWidth, editHeight);
                }
            }

            // Bottom hint line.
            string hint;
            if (_Naming)
            {
                hint = "name — Enter to confirm · Esc cancel";
            }
            else if (_Editing)
            {
                hint = "editing — Esc to stop";
            }
            else
            {
                hint = "←→ profile · Tab field · e/Enter edit · Space activate · a add · r rename · x remove · Esc save & close";
            }

            surface.DrawText(contentX, bottomHintRow, Trim(hint, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
        }

        #endregion

        #region Private-Methods

        private bool HandleNavKey(KeyEvent key)
        {
            switch (key.Code)
            {
                case KeyCode.Escape:
                    FlushEditorToModel();
                    Close(BuildResult());
                    return true;
                case KeyCode.Left:
                    SelectProfile(-1);
                    return true;
                case KeyCode.Right:
                    SelectProfile(1);
                    return true;
                case KeyCode.Tab:
                    CycleField((key.Modifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
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
                    case ' ': ActivateSelected(); return true;
                    case 'a': BeginAdd(); return true;
                    case 'r': BeginRename(); return true;
                    case 'x': RemoveSelected(); return true;
                }
            }

            return true;
        }

        private bool HandleEditKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                FlushEditorToModel();
                _Editing = false;
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

        private bool HandleNamingKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                CommitName();
                return true;
            }

            if (key.Code == KeyCode.Escape)
            {
                _Naming = false;
                return true;
            }

            _Name.HandleKey(key);
            return true;
        }

        private void SelectProfile(int delta)
        {
            if (_Profiles.Count == 0)
            {
                return;
            }

            FlushEditorToModel();
            int count = _Profiles.Count;
            _SelectedProfile = ((_SelectedProfile + delta) % count + count) % count;
            LoadFieldIntoEditor();
        }

        private void CycleField(int delta)
        {
            FlushEditorToModel();
            int count = 3;
            _Field = (PromptField)((((int)_Field + delta) % count + count) % count);
            LoadFieldIntoEditor();
        }

        private void BeginEdit()
        {
            _Editing = true;
        }

        private void ActivateSelected()
        {
            if (_SelectedProfile < 0 || _SelectedProfile >= _Profiles.Count)
            {
                return;
            }

            for (int i = 0; i < _Profiles.Count; i++)
            {
                _Profiles[i].IsActive = i == _SelectedProfile;
            }
        }

        private void BeginAdd()
        {
            FlushEditorToModel();
            _NamingRename = false;
            _Name.Value = string.Empty;
            _Naming = true;
        }

        private void BeginRename()
        {
            if (_SelectedProfile < 0 || _SelectedProfile >= _Profiles.Count)
            {
                return;
            }

            FlushEditorToModel();
            _NamingRename = true;
            _Name.Value = _Profiles[_SelectedProfile].Name ?? string.Empty;
            _Naming = true;
        }

        private void CommitName()
        {
            string name = (_Name.Value ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _Naming = false;
                return;
            }

            if (_NamingRename)
            {
                if (_SelectedProfile >= 0 && _SelectedProfile < _Profiles.Count)
                {
                    _Profiles[_SelectedProfile].Name = name;
                }
            }
            else
            {
                _Profiles.Add(new PromptProfile { Name = name });
                _SelectedProfile = _Profiles.Count - 1;
                LoadFieldIntoEditor();
            }

            _Naming = false;
        }

        private void RemoveSelected()
        {
            if (_Profiles.Count <= 1)
            {
                return;
            }

            if (_SelectedProfile < 0 || _SelectedProfile >= _Profiles.Count)
            {
                return;
            }

            bool removedActive = _Profiles[_SelectedProfile].IsActive;
            _Profiles.RemoveAt(_SelectedProfile);

            if (_SelectedProfile >= _Profiles.Count)
            {
                _SelectedProfile = _Profiles.Count - 1;
            }

            if (removedActive)
            {
                for (int i = 0; i < _Profiles.Count; i++)
                {
                    _Profiles[i].IsActive = i == 0;
                }
            }

            LoadFieldIntoEditor();
        }

        private void LoadFieldIntoEditor()
        {
            if (_SelectedProfile < 0 || _SelectedProfile >= _Profiles.Count)
            {
                _Editor.Text = string.Empty;
                return;
            }

            _Editor.Text = GetFieldText(_Profiles[_SelectedProfile], _Field);
        }

        private void FlushEditorToModel()
        {
            if (_SelectedProfile < 0 || _SelectedProfile >= _Profiles.Count)
            {
                return;
            }

            SetFieldText(_Profiles[_SelectedProfile], _Field, _Editor.Text);
        }

        private List<PromptProfile> BuildResult()
        {
            // Guarantee exactly one active profile: if none is marked, activate the first.
            bool anyActive = false;
            for (int i = 0; i < _Profiles.Count; i++)
            {
                if (_Profiles[i].IsActive)
                {
                    anyActive = true;
                    break;
                }
            }

            if (!anyActive && _Profiles.Count > 0)
            {
                _Profiles[0].IsActive = true;
            }

            return _Profiles;
        }

        private static string GetFieldText(PromptProfile profile, PromptField field)
        {
            switch (field)
            {
                case PromptField.ToolsDisabled: return profile.ToolsDisabledPrompt ?? string.Empty;
                case PromptField.Compaction: return profile.CompactionPrompt ?? string.Empty;
                default: return profile.SystemPrompt ?? string.Empty;
            }
        }

        private static void SetFieldText(PromptProfile profile, PromptField field, string text)
        {
            switch (field)
            {
                case PromptField.ToolsDisabled: profile.ToolsDisabledPrompt = text; break;
                case PromptField.Compaction: profile.CompactionPrompt = text; break;
                default: profile.SystemPrompt = text; break;
            }
        }

        private void RenderProfileList(ISurface surface, int contentX, int y, int width)
        {
            CellStyle label = CellStyle.Default.WithForeground(Color.FromPalette(8));
            surface.DrawText(contentX, y, "Profiles:", label);

            int x = contentX + 10;
            int limit = contentX + width;
            for (int i = 0; i < _Profiles.Count; i++)
            {
                if (x >= limit)
                {
                    break;
                }

                bool selected = i == _SelectedProfile;
                string marker = _Profiles[i].IsActive ? "● " : "  ";
                string name = string.IsNullOrEmpty(_Profiles[i].Name) ? "(unnamed)" : _Profiles[i].Name;
                string chunk = marker + name + "  ";

                int room = limit - x;
                string draw = chunk.Length <= room ? chunk : chunk.Substring(0, Math.Max(0, room));

                CellStyle style = selected
                    ? CellStyle.Default.WithForeground(Color.FromPalette(6)).WithAttribute(CellAttributes.Reverse, true)
                    : CellStyle.Default.WithForeground(Color.FromPalette(7));
                surface.DrawText(x, y, draw, style);
                x += chunk.Length;
            }
        }

        private void RenderFieldTabs(ISurface surface, int contentX, int y, int width)
        {
            CellStyle label = CellStyle.Default.WithForeground(Color.FromPalette(8));
            surface.DrawText(contentX, y, "Field:", label);

            int x = contentX + 7;
            int limit = contentX + width;

            RenderFieldTab(surface, ref x, limit, y, "System", _Field == PromptField.System);
            RenderFieldTab(surface, ref x, limit, y, "Tools-disabled", _Field == PromptField.ToolsDisabled);
            RenderFieldTab(surface, ref x, limit, y, "Compaction", _Field == PromptField.Compaction);
        }

        private static void RenderFieldTab(ISurface surface, ref int x, int limit, int y, string name, bool current)
        {
            if (x >= limit)
            {
                return;
            }

            string chunk = current ? "[" + name + "]  " : " " + name + "   ";
            int room = limit - x;
            string draw = chunk.Length <= room ? chunk : chunk.Substring(0, Math.Max(0, room));

            CellStyle style = current
                ? CellStyle.Default.WithForeground(Color.FromPalette(11)).WithAttribute(CellAttributes.Bold, true)
                : CellStyle.Default.WithForeground(Color.FromPalette(7));
            surface.DrawText(x, y, draw, style);
            x += chunk.Length;
        }

        private void RenderEditor(ISurface surface, int contentX, int top, int width, int height)
        {
            // The TextEditor renders into a BufferSurface, so render it to a buffer and copy the cells into
            // the modal box (mirrors how the app's composer is rendered into a sub-region).
            _Editor.IsFocused = _Editing;
            CellBuffer buffer = new CellBuffer(width, height);
            _Editor.Render(new BufferSurface(buffer));
            for (int yy = 0; yy < height; yy++)
            {
                for (int xx = 0; xx < width; xx++)
                {
                    surface.Set(contentX + xx, top + yy, buffer.Get(xx, yy));
                }
            }
        }

        private void RenderNaming(ISurface surface, int contentX, int top, int width, int height)
        {
            CellStyle label = CellStyle.Default.WithForeground(Color.FromPalette(11)).WithAttribute(CellAttributes.Bold, true);
            surface.DrawText(contentX, top, "Name:", label);

            int fieldX = contentX + 6;
            int fieldWidth = Math.Max(1, width - 6);

            _Name.IsFocused = true;
            CellBuffer buffer = new CellBuffer(fieldWidth, 1);
            _Name.Render(new BufferSurface(buffer));
            for (int xx = 0; xx < fieldWidth; xx++)
            {
                surface.Set(fieldX + xx, top, buffer.Get(xx, 0));
            }
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
