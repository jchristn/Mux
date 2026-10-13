namespace Mux.Cli.App
{
    using System;
    using TUIKit;
    using TUIKit.Modals;

    /// <summary>Renders TUIKit toast messages with mux severity backgrounds.</summary>
    public static class SeverityToastRenderer
    {
        /// <summary>Draws active notifications in the top-right corner without taking focus.</summary>
        /// <param name="surface">The screen surface.</param>
        /// <param name="notifications">The notification center, including expiration and stacking limits.</param>
        /// <param name="nowMilliseconds">The application's monotonic clock.</param>
        public static void Render(ISurface surface, NotificationCenter notifications, long nowMilliseconds)
        {
            ArgumentNullException.ThrowIfNull(surface);
            ArgumentNullException.ThrowIfNull(notifications);
            int width = surface.Size.Width, height = surface.Size.Height;
            if (width <= 0 || height <= 0) return;
            int nextRow = 0;
            foreach (Notification notification in notifications.Active(nowMilliseconds))
            {
                if (nextRow >= height) break;
                // Use TUIKit's own framing, Unicode clipping, and message layout for each toast.
                NotificationCenter single = new NotificationCenter();
                single.Add(notification.Text, notification.Severity, nowMilliseconds, 0);
                CellBuffer buffer = new CellBuffer(width, height - nextRow);
                single.Render(new BufferSurface(buffer), nowMilliseconds);
                CellStyle style = StyleFor(notification.Severity);
                int drawnRows = 0;
                for (int y = 0; y < buffer.Height; y++)
                {
                    int left = width, right = -1;
                    for (int x = 0; x < width; x++)
                    {
                        Cell cell = buffer.Get(x, y);
                        if (cell.Style != CellStyle.Default || (!cell.IsContinuation && cell.Grapheme != " "))
                        { left = Math.Min(left, x); right = Math.Max(right, x + Math.Max(1, cell.Width) - 1); }
                    }
                    if (right < left) continue;
                    surface.Fill(new Rect(left, nextRow + y, right - left + 1, 1), Cell.Blank(style));
                    for (int x = left; x <= right && x < width; x++)
                    {
                        Cell cell = buffer.Get(x, y);
                        if (!cell.IsContinuation) surface.Set(x, nextRow + y, Cell.Glyph(cell.Grapheme, style, cell.Width));
                    }
                    drawnRows = y + 1;
                }
                nextRow += drawnRows;
            }
        }

        private static CellStyle StyleFor(NotificationSeverity severity)
        {
            bool alert = severity == NotificationSeverity.Warning || severity == NotificationSeverity.Error;
            return CellStyle.Default
                .WithBackground(alert ? Color.FromRgb(0xB9, 0x1C, 0x1C) : Color.FromRgb(0x16, 0xA3, 0x4A))
                .WithForeground(severity == NotificationSeverity.Warning ? Color.FromRgb(0xFF, 0xE0, 0x66) : Color.FromRgb(0xFF, 0xFF, 0xFF));
        }
    }
}
