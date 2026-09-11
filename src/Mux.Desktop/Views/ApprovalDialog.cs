namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Core.Models;

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

            Title = "Approve tool";
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            Height = 320;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            FontFamily mono = new FontFamily("Cascadia Mono,Consolas,Menlo,monospace");

            StackPanel panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

            panel.Children.Add(new TextBlock
            {
                Text = "The agent wants to run a tool that can modify files or run commands:",
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

            Button deny = new Button { Content = "Deny" };
            deny.Click += (sender, args) => Close("n");
            buttons.Children.Add(deny);

            Button always = new Button { Content = "Always this session" };
            always.Click += (sender, args) => Close("always");
            buttons.Children.Add(always);

            Button approve = new Button
            {
                Content = "Approve once",
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
