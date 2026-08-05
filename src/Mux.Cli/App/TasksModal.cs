namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Tasks;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;
    using TUIKit.Widgets;

    /// <summary>
    /// A modal that shows the focused job's task plan and lets a human annotate it: change a task's status
    /// and edit its note. Changes are applied directly to the live <see cref="TaskPlan"/> (the single source
    /// of truth), so they persist with the session and are reflected in the sidebar. Escape or Enter (when
    /// not editing a note) closes it.
    /// </summary>
    public sealed class TasksModal : Modal
    {
        #region Private-Members

        private const int PadX = 3;
        private const int PadY = 1;
        private const int ContentWidth = 64;

        private readonly TaskPlan _Plan;
        private readonly TextField _Editor = new TextField();
        private List<AgentTask> _Tasks;
        private int _Selected;
        private bool _EditingNote;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TasksModal"/> class.
        /// </summary>
        /// <param name="plan">The live task plan to view and annotate. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        public TasksModal(TaskPlan plan)
        {
            _Plan = plan ?? throw new ArgumentNullException(nameof(plan));
            _Tasks = new List<AgentTask>(_Plan.Snapshot());
            _Selected = _Tasks.Count > 0 ? 0 : -1;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public override bool HandleKey(KeyEvent key)
        {
            if (_EditingNote)
            {
                return HandleNoteKey(key);
            }

            switch (key.Code)
            {
                case KeyCode.Escape:
                case KeyCode.Enter:
                    Close(null);
                    return true;
                case KeyCode.Up:
                    Move(-1);
                    return true;
                case KeyCode.Down:
                    Move(1);
                    return true;
            }

            if (key.Code == KeyCode.Character)
            {
                switch (char.ToLowerInvariant((char)key.Rune))
                {
                    case 'c': Apply(AgentTaskStatusEnum.Completed); return true;
                    case 'i': Apply(AgentTaskStatusEnum.InProgress); return true;
                    case 'p': Apply(AgentTaskStatusEnum.Pending); return true;
                    case 'k': Apply(AgentTaskStatusEnum.Skipped); return true;
                    case 'b': Apply(AgentTaskStatusEnum.Blocked); return true;
                    case 'n': BeginNoteEdit(); return true;
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
            IReadOnlyList<string> hintLines = HintText.Wrap("↑↓ move · c complete · i start · b block · k skip · p pending · n note · Esc done", contentWidth);
            int listRows = Math.Max(1, _Tasks.Count);
            int bodyRows = 1 /* header */ + hintLines.Count /* hint */ + 1 /* blank */ + listRows;
            int boxHeight = Math.Min(screenHeight, bodyRows + 2 + (2 * PadY));
            int boxWidth = Math.Min(screenWidth, contentWidth + 2 + (2 * PadX));

            int boxX = Math.Max(0, (screenWidth - boxWidth) / 2);
            int boxY = Math.Max(0, (screenHeight - boxHeight) / 2);
            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);

            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default.WithForeground(Color.FromPalette(8)), "Tasks");

            int contentX = boxX + 1 + PadX;
            int row = boxY + 1 + PadY;

            surface.DrawText(contentX, row, Trim($"{CompletedCount()}/{_Tasks.Count} complete", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(7)));
            row++;
            foreach (string hintLine in hintLines)
            {
                surface.DrawText(contentX, row, Trim(hintLine, contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
                row++;
            }

            row++; // blank separator before the task list

            if (_Tasks.Count == 0)
            {
                surface.DrawText(contentX, row, Trim("(no task plan for the focused job)", contentWidth), CellStyle.Default.WithForeground(Color.FromPalette(8)));
                return;
            }

            for (int i = 0; i < _Tasks.Count; i++)
            {
                int y = row + i;
                if (y >= boxY + boxHeight - 1 - PadY)
                {
                    break;
                }

                AgentTask task = _Tasks[i];
                bool selected = i == _Selected;
                string marker = selected ? "▶ " : "  ";
                string noteEditing = _EditingNote && selected ? "  — " + _Editor.Value + "▏" : NoteSuffix(task);
                string line = marker + Glyph(task.Status) + " " + task.Title + noteEditing;

                CellStyle style = selected
                    ? CellStyle.Default.WithForeground(Color.FromPalette((byte)(_EditingNote ? 11 : 6)))
                    : CellStyle.Default.WithForeground(StatusColor(task.Status));
                surface.DrawText(contentX, y, Trim(line, contentWidth), style);
            }
        }

        #endregion

        #region Private-Methods

        private bool HandleNoteKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                string note = _Editor.Value.Trim();
                if (_Selected >= 0 && _Selected < _Tasks.Count)
                {
                    AgentTask task = _Tasks[_Selected];
                    _Plan.TryUpdateTask(task.Id, task.Status, note.Length == 0 ? null : note, out _);
                    Reload();
                }

                _EditingNote = false;
                return true;
            }

            if (key.Code == KeyCode.Escape)
            {
                _EditingNote = false;
                return true;
            }

            _Editor.HandleKey(key);
            return true;
        }

        private void Move(int delta)
        {
            if (_Tasks.Count == 0)
            {
                return;
            }

            _Selected = Math.Clamp(_Selected + delta, 0, _Tasks.Count - 1);
        }

        private void Apply(AgentTaskStatusEnum status)
        {
            if (_Selected < 0 || _Selected >= _Tasks.Count)
            {
                return;
            }

            AgentTask task = _Tasks[_Selected];

            // Manual annotation covers progress and completion; failing a task requires a note, which the
            // model supplies during a run, so it is not offered here.
            string? note = string.IsNullOrWhiteSpace(task.Note) ? null : task.Note;
            _Plan.TryUpdateTask(task.Id, status, note, out _);
            Reload();
        }

        private void BeginNoteEdit()
        {
            if (_Selected < 0 || _Selected >= _Tasks.Count)
            {
                return;
            }

            _Editor.Value = _Tasks[_Selected].Note ?? string.Empty;
            _EditingNote = true;
        }

        private void Reload()
        {
            int previous = _Selected;
            _Tasks = new List<AgentTask>(_Plan.Snapshot());
            _Selected = _Tasks.Count == 0 ? -1 : Math.Clamp(previous, 0, _Tasks.Count - 1);
        }

        private int CompletedCount()
        {
            int count = 0;
            foreach (AgentTask task in _Tasks)
            {
                if (task.Status == AgentTaskStatusEnum.Completed) count++;
            }

            return count;
        }

        private static string NoteSuffix(AgentTask task)
        {
            return string.IsNullOrWhiteSpace(task.Note) ? string.Empty : "  — " + task.Note;
        }

        private static string Glyph(AgentTaskStatusEnum status)
        {
            switch (status)
            {
                case AgentTaskStatusEnum.Completed: return "✔";
                case AgentTaskStatusEnum.InProgress: return "◼";
                case AgentTaskStatusEnum.Failed: return "✗";
                case AgentTaskStatusEnum.Skipped: return "⊘";
                case AgentTaskStatusEnum.Blocked: return "▦";
                default: return "◻";
            }
        }

        private static Color StatusColor(AgentTaskStatusEnum status)
        {
            switch (status)
            {
                case AgentTaskStatusEnum.Completed: return Color.FromPalette(2);
                case AgentTaskStatusEnum.InProgress: return Color.FromPalette(3);
                case AgentTaskStatusEnum.Failed: return Color.FromPalette(1);
                case AgentTaskStatusEnum.Blocked: return Color.FromPalette(3);
                default: return Color.FromPalette(7);
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
