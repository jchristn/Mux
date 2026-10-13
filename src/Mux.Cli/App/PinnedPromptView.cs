namespace Mux.Cli.App
{
    using System.Text;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Unicode;
    using TUIKit.Widgets;

    /// <summary>Shows the beginning of a pinned prompt, marking any omitted text.</summary>
    internal sealed class PinnedPromptView : IWidget
    {
        private readonly Pane _Pane;
        public PinnedPromptView(Pane pane) { _Pane = pane; }
        public Size Measure(Size available) => _Pane.Measure(available);
        public void Render(ISurface surface)
        {
            int width = surface.Size.Width;
            if (width <= 0 || surface.Size.Height <= 0) return;
            surface.Fill(new Rect(0, 0, width, surface.Size.Height), Cell.Blank(_Pane.Background));
            var lines = _Pane.SnapshotPlainLines();
            if (lines.Count == 0) return;
            string first = lines[0];
            bool truncated = lines.Count > 1 || Graphemes.MeasureWidth(first) > width;
            string suffix = truncated ? " ..." : string.Empty;
            if (width < suffix.Length) suffix = new string('.', width);
            int available = width - suffix.Length;
            StringBuilder prefix = new StringBuilder();
            int used = 0;
            foreach (Grapheme grapheme in Graphemes.Split(first))
            {
                if (used + grapheme.Width > available) break;
                prefix.Append(grapheme.Text);
                used += grapheme.Width;
            }
            surface.DrawText(0, 0, prefix.ToString().TrimEnd() + suffix, _Pane.Background);
        }
    }
}
