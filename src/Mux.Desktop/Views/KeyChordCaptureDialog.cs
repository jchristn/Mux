namespace Mux.Desktop.Views
{
    using System;
    using System.Collections.Generic;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Input;
    using Avalonia.Layout;
    using Avalonia.Media;

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

            Title = "Rebind key";
            Icon = IconResources.LoadWindowIcon();
            Width = 440;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = theme.Surface;

            StackPanel panel = new StackPanel { Margin = new Thickness(24), Spacing = 14 };
            panel.Children.Add(new TextBlock { Text = "Press the key combination for \"" + commandTitle + "\".", Foreground = theme.Text, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = "Current: " + (string.IsNullOrEmpty(currentChord) ? "unbound" : currentChord), Foreground = theme.Muted, FontSize = 12 });

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
            Button cancel = new Button { Content = "Cancel" };
            cancel.Tip("Keep the current binding.");
            cancel.Click += (sender, args) => Close(null);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = "Assign", Background = theme.AccentButton, Foreground = theme.AccentText, IsEnabled = false };
            ok.Tip("Assign the captured chord.");
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

            if (IsModifierKey(e.Key))
            {
                return;
            }

            string? token = KeyToken(e.Key);
            if (token == null)
            {
                return;
            }

            e.Handled = true;
            _Captured = FormatChord(e.KeyModifiers, token);
            _Preview.Text = _Captured;
            assign.IsEnabled = true;
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl
                || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift
                || key == Key.LWin || key == Key.RWin;
        }

        private static string FormatChord(KeyModifiers modifiers, string token)
        {
            List<string> parts = new List<string>();
            if ((modifiers & KeyModifiers.Control) != 0)
            {
                parts.Add("ctrl");
            }

            if ((modifiers & KeyModifiers.Alt) != 0)
            {
                parts.Add("alt");
            }

            if ((modifiers & KeyModifiers.Shift) != 0)
            {
                parts.Add("shift");
            }

            parts.Add(token);
            return string.Join("+", parts);
        }

        private static string? KeyToken(Key key)
        {
            if (key >= Key.A && key <= Key.Z)
            {
                return key.ToString().ToLowerInvariant();
            }

            if (key >= Key.D0 && key <= Key.D9)
            {
                return ((int)(key - Key.D0)).ToString();
            }

            if (key >= Key.F1 && key <= Key.F24)
            {
                return "f" + (int)(key - Key.F1 + 1);
            }

            switch (key)
            {
                case Key.Enter: return "enter";
                case Key.Space: return "space";
                case Key.Tab: return "tab";
                case Key.Back: return "backspace";
                case Key.Delete: return "delete";
                case Key.Insert: return "insert";
                case Key.Home: return "home";
                case Key.End: return "end";
                case Key.PageUp: return "pageup";
                case Key.PageDown: return "pagedown";
                case Key.Up: return "up";
                case Key.Down: return "down";
                case Key.Left: return "left";
                case Key.Right: return "right";
                default: return null;
            }
        }
    }
}
