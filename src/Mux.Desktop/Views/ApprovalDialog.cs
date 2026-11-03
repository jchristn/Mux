namespace Mux.Desktop.Views
{
    using System;
    using System.Threading;
    using Avalonia.Threading;
    using Mux.Core.Approvals;
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
        /// <param name="seconds">Auto-approval seconds; zero disables.</param>
        /// <param name="cancellationToken">Cancels the pending approval.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolCall"/> is null.</exception>
        public ApprovalDialog(ToolCall toolCall, int seconds = 0, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(toolCall);

            Title = Localizer.T("approval.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 520;
            Height = 360;
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

            TextBlock countdownText = new TextBlock();
            panel.Children.Add(countdownText);
            ApprovalCountdown countdown = new ApprovalCountdown(seconds);
            DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            CancellationTokenRegistration cancellation = default;
            void Tick()
            {
                if (cancellationToken.IsCancellationRequested) { Close("n"); return; }
                if (countdown.Update(IsActive)) { Close("y"); return; }
                countdownText.Text = seconds > 0 ? "Auto-approve in " + countdown.RemainingSeconds + "s" : "Automatic approval disabled";
            }
            timer.Tick += (_, _) => Tick();
            Opened += (_, _) =>
            {
                cancellation = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => { if (IsVisible) Close("n"); }));
                Tick();
                if (IsVisible) timer.Start();
            };
            Closed += (_, _) => { timer.Stop(); cancellation.Dispose(); };
            panel.Children.Add(buttons);
            Content = panel;
        }
    }
}
