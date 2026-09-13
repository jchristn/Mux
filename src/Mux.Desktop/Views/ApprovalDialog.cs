namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;
    using Mux.Desktop.I18n;

    /// <summary>
    /// A modal dialog asking the user to approve a proposed mutating tool call. Returns "y" (approve once),
    /// "always" (approve the rest of the session), or "n" (deny) via <c>ShowDialog&lt;string&gt;</c>. Closing
    /// the dialog without choosing denies.
    /// </summary>
    public sealed class ApprovalDialog : Window
    {
        /// <summary>
        /// Instantiate the approval dialog for a proposed tool call.
        /// </summary>
        /// <param name="toolCall">The proposed tool call. Required.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolCall"/> is null.</exception>
        public ApprovalDialog(ToolCall toolCall)
        {
            ArgumentNullException.ThrowIfNull(toolCall);

            Title = Localizer.T("approval.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            Height = 320;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            FontFamily mono = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace");

            StackPanel panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

            panel.Children.Add(new TextBlock
            {
                Text = Localizer.T("approval.body"),
                TextWrapping = TextWrapping.Wrap
            });

            panel.Children.Add(new TextBlock
            {
                Text = toolCall.Name,
                FontFamily = mono,
                FontWeight = FontWeight.SemiBold,
                FontSize = 15
            });

            TextBox arguments = new TextBox
            {
                Text = toolCall.Arguments,
                FontFamily = mono,
                FontSize = 12,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 140,
                Height = 140
            };
            panel.Children.Add(arguments);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            Button deny = new Button { Content = Localizer.T("approval.deny") };
            deny.Click += (sender, args) => Close("n");
            buttons.Children.Add(deny);

            Button always = new Button { Content = Localizer.T("approval.always") };
            always.Click += (sender, args) => Close("always");
            buttons.Children.Add(always);

            Button approve = new Button
            {
                Content = Localizer.T("approval.approveOnce"),
                Background = AppTheme.Current.AccentButton,
                Foreground = AppTheme.Current.AccentText
            };
            approve.Click += (sender, args) => Close("y");
            buttons.Children.Add(approve);

            panel.Children.Add(buttons);
            Content = panel;
        }
    }
}
