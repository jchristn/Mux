namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A modal that captures a single key chord by listening for a key press and formatting it as a
    /// lowercase <c>modifier+key</c> string (for example <c>ctrl+k</c> or <c>f12</c>) compatible with the
    /// TUI's chord parser. Pure modifier presses are ignored until a non-modifier key completes the chord.
    /// Returns the captured chord via <c>ShowDialog&lt;string?&gt;</c>, or null when cancelled.
    /// </summary>
    public sealed class KeyChordCaptureDialog : Window
    {
        private readonly TextBlock _Preview;
        private string? _Captured;

        /// <summary>
        /// Instantiate the capture dialog.
        /// </summary>
        /// <param name="commandTitle">The title of the command being rebound (shown in the prompt). Required.</param>
        /// <param name="currentChord">The command's current chord, shown as a hint; null shows "unbound".</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="commandTitle"/> is null.</exception>
        public KeyChordCaptureDialog(string commandTitle, string? currentChord)
        {
            ArgumentNullException.ThrowIfNull(commandTitle);

            AppTheme theme = AppTheme.Current;

            Title = Localizer.T("keybinding.capture.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            StackPanel panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
            panel.Children.Add(new TextBlock { Text = Localizer.T("keybinding.capture.prompt") + " \"" + commandTitle + "\".", Foreground = theme.Text, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = Localizer.T("keybinding.capture.current") + " " + (string.IsNullOrEmpty(currentChord) ? Localizer.T("keybinding.capture.unbound") : currentChord), Foreground = theme.Muted, FontSize = 12 });

            _Preview = new TextBlock
            {
                Text = "…",
                Foreground = theme.Text,
                FontSize = 20,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 6)
            };
            panel.Children.Add(new Border { Background = theme.SurfaceAlt, BorderBrush = theme.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 16, 12, 16), Child = _Preview });

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("keybinding.capture.cancel.tip"));
            cancel.Click += (sender, args) => Close(null);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("keybinding.assign"), Background = theme.AccentButton, Foreground = theme.AccentText, IsEnabled = false };
            ok.Tip(Localizer.T("keybinding.assign.tip"));
            ok.Click += (sender, args) => Close(_Captured);
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            Content = panel;

            AddHandler(KeyDownEvent, (sender, args) => OnKey(args, ok), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void OnKey(KeyEventArgs e, Button assign)
        {
            if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                Close(null);
                return;
            }

            if (KeyChordFormat.IsModifierKey(e.Key))
            {
                return;
            }

            string? chord = KeyChordFormat.Format(e.KeyModifiers, e.Key);
            if (chord == null)
            {
                return;
            }

            e.Handled = true;
            _Captured = chord;
            _Preview.Text = _Captured;
            assign.IsEnabled = true;
        }
    }
}
