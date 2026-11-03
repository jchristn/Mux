namespace Mux.Cli.App
{
    using System;
    using Mux.Core.Approvals;
    using TUIKit;
    using TUIKit.Input;
    using TUIKit.Modals;

    /// <summary>An approval prompt whose countdown advances only while it is visible and topmost.</summary>
    internal sealed class ToolApprovalModal : Modal
    {
        private readonly string _Name;
        private readonly int _Seconds;
        private readonly ApprovalCountdown _Countdown;
        private int _Selected;
        internal ToolApprovalModal(string name, int seconds, TimeProvider? clock = null)
        {
            _Name = name;
            _Seconds = seconds;
            _Countdown = new ApprovalCountdown(seconds, clock);
        }
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape) RequestClose(-1);
            else if (key.Code == KeyCode.Enter) RequestClose(_Selected);
            else if (key.Code == KeyCode.Up) _Selected = Math.Max(0, _Selected - 1);
            else if (key.Code == KeyCode.Down) _Selected = Math.Min(2, _Selected + 1);
            return true;
        }
        public override void Render(ISurface surface)
        {
            if (IsClosed) return;
            int width = Math.Min(64, surface.Size.Width);
            int height = Math.Min(9, surface.Size.Height);
            bool visible = IsTopmost && width >= 30 && height >= 9;
            if (_Countdown.Update(visible)) { RequestClose(0); return; }
            int x = (surface.Size.Width - width) / 2;
            int y = (surface.Size.Height - height) / 2;
            Rect box = new Rect(x, y, width, height);
            surface.Fill(box, Cell.Blank(CellStyle.Default));
            surface.DrawBox(box, CellStyle.Default, "Approve " + _Name + "?");
            string[] options = { "Approve once", "Deny", "Always allow this session" };
            for (int i = 0; i < options.Length; i++)
                surface.DrawText(x + 2, y + 2 + i, (i == _Selected ? "> " : "  ") + options[i], CellStyle.Default);
            surface.DrawText(x + 2, y + 6, _Seconds > 0 ? "Auto-approve in " + _Countdown.RemainingSeconds + "s" : "Automatic approval disabled", CellStyle.Default);
            surface.DrawText(x + 2, y + 7, "Enter selects / Esc denies", CellStyle.Default);
        }
    }
}
