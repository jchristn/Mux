namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input.Platform;
    using Avalonia.Media;
    using Avalonia.Threading;

    /// <summary>
    /// The reusable copy-to-clipboard control for the desktop app. Implemented as a factory that returns a
    /// plain <see cref="Button"/> (rather than a <see cref="Button"/> subclass, which in Avalonia would not
    /// inherit the default button control theme and therefore would not render or receive clicks). The
    /// returned button copies the text from its provider — evaluated at click time, so each button copies its
    /// own content — to the owning window's clipboard, and flips to a green ✓ immediately on click (before the
    /// clipboard call, so the feedback never depends on the clipboard being fast or available), restoring the
    /// ⧉ glyph after a moment. The clipboard is taken from the supplied owner (a shown window's
    /// <see cref="TopLevel.Clipboard"/> is reliable), falling back to a tree walk from the button.
    /// </summary>
    public static class CopyButton
    {
        /// <summary>
        /// Creates a copy button.
        /// </summary>
        /// <param name="textProvider">Returns the text to copy, evaluated at click time. Required.</param>
        /// <param name="theme">The active theme (idle and success colors). Required.</param>
        /// <param name="clipboardOwner">The window the button lives in, whose clipboard is used. Required.</param>
        /// <returns>A configured button with the copy behavior wired.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public static Button Create(Func<string> textProvider, AppTheme theme, TopLevel clipboardOwner)
        {
            if (textProvider is null) throw new ArgumentNullException(nameof(textProvider));
            if (theme is null) throw new ArgumentNullException(nameof(theme));
            if (clipboardOwner is null) throw new ArgumentNullException(nameof(clipboardOwner));

            Button button = new Button
            {
                Content = "⧉",
                Background = Brushes.Transparent,
                Foreground = theme.Muted,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 2, 6, 2),
                FontSize = 13
            };
            button.Tip("Copy to the clipboard.");

            DispatcherTimer? resetTimer = null;
            button.Click += async (sender, args) =>
            {
                // Immediate feedback, before any await, so the ✓ shows even if the clipboard call is slow or
                // unavailable. The reset runs on a timer independent of the copy.
                button.Content = "✓";
                button.Foreground = theme.Success;
                resetTimer?.Stop();
                resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                resetTimer.Tick += (ts, te) =>
                {
                    resetTimer?.Stop();
                    resetTimer = null;
                    button.Content = "⧉";
                    button.Foreground = theme.Muted;
                };
                resetTimer.Start();

                string text = string.Empty;
                try
                {
                    text = textProvider() ?? string.Empty;
                }
                catch (Exception)
                {
                    // Provider failure copies nothing but still gives feedback.
                }

                // Never write an empty string — that would clear whatever is on the clipboard.
                if (text.Length == 0)
                {
                    return;
                }

                try
                {
                    IClipboard? clipboard = clipboardOwner.Clipboard ?? TopLevel.GetTopLevel(button)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                }
                catch (Exception)
                {
                    // Best-effort copy.
                }
            };

            return button;
        }
    }
}
