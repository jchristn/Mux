namespace Mux.Agent
{
    using System;
    using System.IO;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Platform;
    using Avalonia.Themes.Fluent;

    /// <summary>
    /// The Avalonia application for the mux tray agent. Its only surface is the system-tray icon with three
    /// actions — About, Launch Mux, and Exit — over a background <see cref="AgentHost"/> that runs the local
    /// REST server.
    /// </summary>
    public sealed class App : Application
    {
        private AgentHost? _Host;
        private TrayIcon? _Tray;

        /// <summary>
        /// Initialize application-level styles.
        /// </summary>
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        /// <summary>
        /// Complete framework initialization: start the host and build the tray.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            _Host = new AgentHost();
            try
            {
                _Host.Start();
            }
            catch (Exception)
            {
                // The tray stays up even if the server fails to bind; About/Exit still work.
            }

            BuildTray();
            base.OnFrameworkInitializationCompleted();
        }

        private void BuildTray()
        {
            NativeMenu menu = new NativeMenu();

            NativeMenuItem about = new NativeMenuItem("About");
            about.Click += OnAbout;
            menu.Items.Add(about);

            NativeMenuItem launch = new NativeMenuItem("Launch Mux");
            launch.Click += OnLaunch;
            menu.Items.Add(launch);

            NativeMenuItem dashboard = new NativeMenuItem("Launch Dashboard");
            dashboard.Click += OnLaunchDashboard;
            menu.Items.Add(dashboard);

            menu.Items.Add(new NativeMenuItemSeparator());

            NativeMenuItem exit = new NativeMenuItem("Exit");
            exit.Click += OnExit;
            menu.Items.Add(exit);

            _Tray = new TrayIcon();
            _Tray.ToolTipText = string.IsNullOrEmpty(_Host?.BaseUrl) ? "mux agent" : "mux — " + _Host!.BaseUrl;
            _Tray.Icon = LoadIcon();
            _Tray.Menu = menu;
            _Tray.IsVisible = true;

            // Keep the tray glyph readable when the OS switches between light and dark: dark icon on a light
            // taskbar, white icon on a dark taskbar.
            IPlatformSettings? platformSettings = CurrentPlatformSettings;
            if (platformSettings != null)
            {
                platformSettings.ColorValuesChanged += (sender, args) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        if (_Tray != null) _Tray.Icon = LoadIcon();
                    });
                };
            }
        }

        internal static WindowIcon? LoadIcon()
        {
            // The taskbar/notification area follows the OS theme. Use the white glyph on a dark taskbar and the
            // black glyph on a light one. Load the full-resolution PNG (not the tiny .ico) so the icon renders
            // crisp and full-size rather than small and blurry.
            bool dark = false;
            try
            {
                IPlatformSettings? platformSettings = Application.Current?.PlatformSettings;
                if (platformSettings != null)
                {
                    dark = platformSettings.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;
                }
            }
            catch (Exception)
            {
            }

            string resource = dark ? "Mux.Agent.logo-white.png" : "Mux.Agent.logo-black.png";
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
                if (stream == null) return null;
                using (stream)
                {
                    return new WindowIcon(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IPlatformSettings? CurrentPlatformSettings
        {
            get
            {
                try { return Application.Current?.PlatformSettings; }
                catch (Exception) { return null; }
            }
        }

        private void OnAbout(object? sender, EventArgs e)
        {
            AboutWindow window = new AboutWindow();
            window.Show();
        }

        private void OnLaunch(object? sender, EventArgs e)
        {
            _Host?.LaunchMux();
        }

        private void OnLaunchDashboard(object? sender, EventArgs e)
        {
            _Host?.LaunchDashboard();
        }

        private void OnExit(object? sender, EventArgs e)
        {
            _Host?.Stop();
            if (_Tray != null) _Tray.IsVisible = false;

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
    }
}
