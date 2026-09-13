namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A small modal confirmation. Returns true (confirmed) or false (cancelled) via
    /// <c>ShowDialog&lt;bool&gt;</c>. The confirm button is styled as destructive when requested.
    /// </summary>
    public sealed class ConfirmDialog : Window
    {
        /// <summary>
        /// Instantiate the confirmation dialog.
        /// </summary>
        /// <param name="title">The window title. Required.</param>
        /// <param name="message">The message body. Required.</param>
        /// <param name="confirmLabel">The confirm button label. Required.</param>
        /// <param name="destructive">Whether the confirm action is destructive (red).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public ConfirmDialog(string title, string message, string confirmLabel, bool destructive)
        {
            ArgumentNullException.ThrowIfNull(title);
            ArgumentNullException.ThrowIfNull(message);
            ArgumentNullException.ThrowIfNull(confirmLabel);

            Title = title;
            Icon = IconResources.LoadWindowIcon();
            Width = 460;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
            panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            Button cancel = new Button { Content = Localizer.T("act.cancel") };
            cancel.Tip(Localizer.T("confirm.cancel.tip"));
            cancel.Click += (sender, args) => Close(false);
            buttons.Children.Add(cancel);

            Button confirm = new Button { Content = confirmLabel, Foreground = AppTheme.Current.AccentText };
            confirm.Background = destructive ? AppTheme.Current.Error : AppTheme.Current.AccentButton;
            confirm.Tip(destructive ? Localizer.T("confirm.destructive.tip") : Localizer.T("confirm.action.tip"));
            confirm.Click += (sender, args) => Close(true);
            buttons.Children.Add(confirm);

            panel.Children.Add(buttons);
            Content = panel;
        }
    }
}
