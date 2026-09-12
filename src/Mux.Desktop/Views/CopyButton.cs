namespace Mux.Desktop.Views
{
    using System;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input.Platform;
    using Avalonia.Interactivity;
    using Avalonia.Media;

    /// <summary>
    /// The reusable copy-to-clipboard control for the desktop app: a small ⧉ button that, when clicked,
    /// copies the text from its provider to this window's clipboard and briefly flips to a green ✓ for
    /// feedback (regardless of whether the text was empty), then restores. The text is read from the provider
    /// at click time so each button copies its own content — a single shared component used everywhere copy
    /// is offered.
    /// </summary>
    public sealed class CopyButton : Button
    {
        private readonly Func<string> _Provider;
        private readonly AppTheme _Theme;
        private bool _Flashing;

        /// <summary>
        /// Instantiate a copy button.
        /// </summary>
        /// <param name="textProvider">Returns the text to copy, evaluated at click time. Required.</param>
        /// <param name="theme">The active theme (for the idle and success colors). Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public CopyButton(Func<string> textProvider, AppTheme theme)
        {
            _Provider = textProvider ?? throw new ArgumentNullException(nameof(textProvider));
            _Theme = theme ?? throw new ArgumentNullException(nameof(theme));

            Content = "⧉";
            Background = Brushes.Transparent;
            Foreground = theme.Muted;
            BorderThickness = new Thickness(0);
            Padding = new Thickness(6, 2, 6, 2);
            FontSize = 13;
            Click += OnClick;
            this.Tip("Copy to the clipboard.");
        }

        private async void OnClick(object? sender, RoutedEventArgs e)
        {
            string text = string.Empty;
            try
            {
                text = _Provider() ?? string.Empty;
            }
            catch (Exception)
            {
                // Provider failure copies nothing but still gives feedback.
            }

            try
            {
                IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(text);
                }
            }
            catch (Exception)
            {
                // Best-effort copy.
            }

            await FlashAsync();
        }

        private async Task FlashAsync()
        {
            if (_Flashing)
            {
                return;
            }

            _Flashing = true;
            Content = "✓";
            Foreground = _Theme.Success;
            try
            {
                await Task.Delay(1200);
            }
            catch (Exception)
            {
                // Ignore.
            }

            Content = "⧉";
            Foreground = _Theme.Muted;
            _Flashing = false;
        }
    }
}
