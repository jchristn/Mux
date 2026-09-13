namespace Mux.Desktop.Views
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;

    /// <summary>
    /// Controls the optional embedded REST + dashboard server (row 42 / §12). A single Start/Stop toggle
    /// drives <see cref="EmbeddedServerService"/>; while running, the window shows the bound base URL, the
    /// dashboard and health URLs, and the local API key (with the bearer-header hint), plus a button to open
    /// the dashboard in the default browser. Off by default — the desktop always drives the engine in-process,
    /// so this is purely opt-in for external REST/dashboard access.
    /// </summary>
    public sealed class LocalServerWindow : Window
    {
        private readonly EmbeddedServerService _Service = EmbeddedServerService.Instance;
        private readonly AppTheme _Theme = AppTheme.Current;
        private readonly StackPanel _Body = new StackPanel { Spacing = 10 };
        private Button _Toggle = new Button();

        /// <summary>
        /// Instantiate the local-server window.
        /// </summary>
        public LocalServerWindow()
        {
            Title = Localizer.T("localServer.title");
            Icon = IconResources.LoadWindowIcon();
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MinWidth = 460;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = _Theme.Surface;

            Content = BuildLayout();
            RenderState();
        }

        private Control BuildLayout()
        {
            StackPanel root = new StackPanel { Margin = new Thickness(22), Spacing = 16 };

            root.Children.Add(new TextBlock { Text = Localizer.T("localServer.title"), FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = _Theme.Text });
            root.Children.Add(new TextBlock
            {
                Text = Localizer.T("localServer.description"),
                Foreground = _Theme.Muted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            });

            _Toggle = new Button { Foreground = _Theme.AccentText, Padding = new Thickness(16, 8, 16, 8), HorizontalAlignment = HorizontalAlignment.Left };
            _Toggle.Click += (sender, args) => OnToggle();
            root.Children.Add(_Toggle);

            root.Children.Add(_Body);
            return root;
        }

        private void OnToggle()
        {
            _Service.Toggle();
            RenderState();
        }

        private void RenderState()
        {
            bool running = _Service.IsRunning;
            _Toggle.Content = running ? Localizer.T("localServer.stop") : Localizer.T("localServer.start");
            _Toggle.Background = running ? _Theme.Error : _Theme.AccentButton;

            _Body.Children.Clear();

            if (!running)
            {
                _Body.Children.Add(new TextBlock { Text = Localizer.T("localServer.stopped"), Foreground = _Theme.Muted, FontSize = 12 });
                string? error = _Service.LastError;
                if (!string.IsNullOrEmpty(error))
                {
                    _Body.Children.Add(new TextBlock { Text = Localizer.T("localServer.lastStartFailed") + error, Foreground = _Theme.Error, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                }

                return;
            }

            _Body.Children.Add(FieldRow(Localizer.T("localServer.status"), Localizer.T("localServer.listening")));
            _Body.Children.Add(FieldRow(Localizer.T("localServer.baseUrl"), _Service.BaseUrl ?? "—"));
            _Body.Children.Add(FieldRow(Localizer.T("localServer.dashboard"), _Service.DashboardUrl ?? "—"));
            _Body.Children.Add(FieldRow(Localizer.T("localServer.health"), _Service.HealthUrl ?? "—"));

            if (_Service.AuthEnabled && !string.IsNullOrEmpty(_Service.ApiKey))
            {
                _Body.Children.Add(FieldRow(Localizer.T("localServer.apiKey"), _Service.ApiKey!));
                _Body.Children.Add(new TextBlock { Text = Localizer.T("localServer.sendAs") + "  Authorization: Bearer <key>", Foreground = _Theme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
            }

            Button open = new Button { Content = Localizer.T("localServer.openDashboard"), Background = _Theme.SurfaceAlt, Foreground = _Theme.Text, BorderBrush = _Theme.Border, BorderThickness = new Thickness(1), Padding = new Thickness(14, 6, 14, 6), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            open.Click += (sender, args) =>
            {
                string? url = _Service.DashboardUrl;
                if (!string.IsNullOrEmpty(url))
                {
                    BrowserLauncher.OpenUrl(url!);
                }
            };
            _Body.Children.Add(open);
        }

        private Control FieldRow(string label, string value)
        {
            DockPanel row = new DockPanel();
            TextBlock name = new TextBlock { Text = label, Foreground = _Theme.Muted, FontSize = 12, Width = 90, VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(name, Dock.Left);
            row.Children.Add(name);
            row.Children.Add(new TextBlock { Text = value, Foreground = _Theme.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Menlo, monospace") });
            return row;
        }
    }
}
