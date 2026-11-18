namespace Mux.Desktop.Views
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Threading;
    using Mux.Core.Cloud;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;

    /// <summary>
    /// Signs this machine in to and out of Mux Cloud and shows the tunnel status, which the tray agent (or whichever
    /// mux process owns the local hub) reports. Signed out, it shows the sign-in form under the remote-access notice;
    /// signed in, it shows the connection, the remote tool approval switch, and Sign out. The status refreshes every
    /// two seconds while the window is open.
    /// </summary>
    public sealed class MuxCloudWindow : Window
    {
        private readonly AppTheme _Theme = AppTheme.Current;
        private readonly StackPanel _Body = new StackPanel { Spacing = 10 };
        private readonly DispatcherTimer _Timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private readonly TextBox _Email = new TextBox();
        private readonly TextBox _Password = new TextBox { PasswordChar = '•' };
        private readonly TextBox _Server = new TextBox();
        private readonly TextBox _MachineName = new TextBox();
        private readonly TextBlock _Message = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        private CloudStatus _Status = new CloudStatus();
        private bool _Busy;
        private bool _SignedInView;
        private bool _Built;

        /// <summary>
        /// Instantiate the Mux Cloud window.
        /// </summary>
        public MuxCloudWindow()
        {
            Title = Localizer.T("cloud.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 580;
            SizeToContent = SizeToContent.Height;
            MinWidth = 480;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = _Theme.Surface;

            MuxSettings settings = SettingsLoader.LoadSettings();
            _Server.Text = settings.Cloud.ServerUrl;
            _MachineName.Text = settings.Cloud.MachineName ?? CloudMachineInfo.Hostname();
            _Email.Text = settings.Cloud.AccountEmail ?? string.Empty;
            _Status = CloudAccountService.OfflineStatus(settings.Cloud, null);

            StackPanel root = new StackPanel { Margin = new Thickness(22), Spacing = 14 };
            root.Children.Add(new TextBlock { Text = Localizer.T("cloud.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = _Theme.Text });
            root.Children.Add(new TextBlock { Text = Localizer.T("cloud.description"), Foreground = _Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(new Border
            {
                Background = _Theme.SurfaceAlt,
                BorderBrush = _Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Child = new TextBlock { Text = Localizer.T("cloud.notice"), Foreground = _Theme.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap }
            });
            root.Children.Add(_Body);
            root.Children.Add(_Message);
            Content = root;

            _Timer.Tick += (sender, args) => _ = RefreshAsync();
            Opened += (sender, args) => { _Timer.Start(); _ = RefreshAsync(); };
            Closed += (sender, args) => _Timer.Stop();
            Render(true);
        }

        private async Task RefreshAsync()
        {
            CloudStatus status = await CloudHubStatusClient.GetOrOfflineAsync(CancellationToken.None).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                _Status = status;
                Render(false);
            });
        }

        private void Render(bool force)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            bool signedIn = settings.Cloud.IsEnrolled;
            if (!force && _Built && signedIn == _SignedInView && !signedIn)
            {
                // Keep a half-typed sign-in form intact between status refreshes.
                return;
            }

            _Built = true;
            _SignedInView = signedIn;
            _Body.Children.Clear();
            if (!signedIn)
            {
                _Body.Children.Add(Labeled(Localizer.T("cloud.email"), _Email));
                _Body.Children.Add(Labeled(Localizer.T("cloud.password"), _Password));
                _Body.Children.Add(Labeled(Localizer.T("cloud.server"), _Server));
                _Body.Children.Add(Labeled(Localizer.T("cloud.machineName"), _MachineName));
                Button signIn = new Button { Content = Localizer.T(_Busy ? "cloud.signingIn" : "cloud.signIn"), IsEnabled = !_Busy, Background = _Theme.AccentButton, Foreground = _Theme.AccentText, Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Left };
                signIn.Click += (sender, args) => _ = SignInAsync();
                _Body.Children.Add(signIn);
                if (_Status.State == CloudStateEnum.SignedOut || _Status.State == CloudStateEnum.Revoked)
                {
                    _Body.Children.Add(FieldRow(Localizer.T("cloud.status"), _Status.StateName + ": " + _Status.Describe()));
                }

                return;
            }

            _Body.Children.Add(FieldRow(Localizer.T("cloud.status"), _Status.StateName));
            _Body.Children.Add(new TextBlock { Text = _Status.Describe(), Foreground = _Status.State == CloudStateEnum.Connected ? _Theme.Text : _Theme.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            _Body.Children.Add(FieldRow(Localizer.T("cloud.server"), settings.Cloud.ServerUrl));
            _Body.Children.Add(FieldRow(Localizer.T("cloud.machine"), (settings.Cloud.MachineName ?? CloudMachineInfo.Hostname()) + "  (" + settings.Cloud.MachineId + ")"));
            _Body.Children.Add(FieldRow(Localizer.T("cloud.account"), settings.Cloud.AccountEmail ?? string.Empty));

            CheckBox remote = new CheckBox { Content = Localizer.T("cloud.remoteApproval"), IsChecked = settings.Cloud.AllowRemoteToolApproval, Foreground = _Theme.Text };
            remote.IsCheckedChanged += (sender, args) =>
            {
                MuxSettings latest = SettingsLoader.LoadSettings();
                latest.Cloud.AllowRemoteToolApproval = remote.IsChecked == true;
                SettingsLoader.SaveSettings(latest);
            };
            _Body.Children.Add(remote);

            StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            Button open = new Button { Content = Localizer.T("cloud.open"), Background = _Theme.SurfaceAlt, Foreground = _Theme.Text, BorderBrush = _Theme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(14, 6, 14, 6) };
            open.Click += (sender, args) => BrowserLauncher.OpenUrl(settings.Cloud.ServerUrl);
            Button signOut = new Button { Content = Localizer.T("cloud.signOut"), Background = _Theme.Error, Foreground = _Theme.AccentText, Padding = new Thickness(14, 6, 14, 6) };
            signOut.Click += (sender, args) =>
            {
                CloudAccountService.Logout();
                _Message.Text = string.Empty;
                Render(true);
                _ = RefreshAsync();
            };
            buttons.Children.Add(open);
            buttons.Children.Add(signOut);
            _Body.Children.Add(buttons);
        }

        private async Task SignInAsync()
        {
            string email = (_Email.Text ?? string.Empty).Trim();
            string password = _Password.Text ?? string.Empty;
            if (email.Length == 0 || password.Length == 0)
            {
                _Message.Foreground = _Theme.Error;
                _Message.Text = Localizer.T("cloud.required");
                return;
            }

            _Busy = true;
            _Message.Text = string.Empty;
            Render(true);
            string? server = string.IsNullOrWhiteSpace(_Server.Text) ? null : _Server.Text.Trim();
            string? name = string.IsNullOrWhiteSpace(_MachineName.Text) ? null : _MachineName.Text.Trim();
            string? error = null;
            try
            {
                await Task.Run(() => CloudAccountService.LoginAsync(email, password, server, name, CancellationToken.None)).ConfigureAwait(false);
                Mux.Core.Startup.AgentLauncher.EnsureRunning();
            }
            catch (Exception ex) when (ex is CloudEnrollmentException || ex is ArgumentException)
            {
                error = ex.Message;
            }

            Dispatcher.UIThread.Post(() =>
            {
                _Busy = false;
                _Password.Text = string.Empty;
                if (error != null)
                {
                    _Message.Foreground = _Theme.Error;
                    _Message.Text = Localizer.T("cloud.failed") + error;
                }

                Render(true);
                _ = RefreshAsync();
            });
        }

        private Control Labeled(string label, Control field)
        {
            DockPanel row = new DockPanel();
            TextBlock name = new TextBlock { Text = label, Foreground = _Theme.Muted, FontSize = 12, Width = 110, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            row.Children.Add(field);
            return row;
        }

        private Control FieldRow(string label, string value)
        {
            DockPanel row = new DockPanel();
            TextBlock name = new TextBlock { Text = label, Foreground = _Theme.Muted, FontSize = 12, Width = 110, VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            row.Children.Add(new TextBlock { Text = value, Foreground = _Theme.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            return row;
        }
    }
}
