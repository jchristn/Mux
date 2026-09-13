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
    /// A small modal that collects a single line of text. Returns the entered text via
    /// <c>ShowDialog&lt;string?&gt;</c>, or null when cancelled.
    /// </summary>
    public sealed class InputDialog : Window
    {
        private readonly TextBox _Input = new TextBox { VerticalContentAlignment = VerticalAlignment.Center };

        /// <summary>
        /// Instantiate the input dialog.
        /// </summary>
        /// <param name="title">The window title. Required.</param>
        /// <param name="prompt">The prompt shown above the input. Required.</param>
        /// <param name="initialText">The initial text; null starts empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public InputDialog(string title, string prompt, string? initialText)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(prompt);

            Title = title;
            Icon = IconResources.LoadWindowIcon();
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            _Input.Text = initialText ?? string.Empty;
            _Input.KeyDown += OnInputKeyDown;

            StackPanel panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(_Input);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("input.cancel.tip"));
            cancel.Click += (sender, args) => Close(null);
            buttons.Children.Add(cancel);
            Button ok = new Button { Content = Localizer.T("input.ok"), Background = AppTheme.Current.AccentButton, Foreground = AppTheme.Current.AccentText };
            ok.Tip(Localizer.T("input.ok.tip"));
            ok.Click += (sender, args) => Submit();
            buttons.Children.Add(ok);
            panel.Children.Add(buttons);

            Content = panel;
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Submit();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close(null);
            }
        }

        private void Submit()
        {
            string text = (_Input.Text ?? string.Empty).Trim();
            Close(text.Length == 0 ? null : text);
        }
    }
}
