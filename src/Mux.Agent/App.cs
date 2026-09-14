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
    /// The Avalonia application for the mux tray agent. Its only surface is the system-tray icon, which
    /// launches each mux front end — Launch Dashboard, Launch Terminal, Launch Desktop — plus About and Exit,
    /// over a background <see cref="AgentHost"/> that runs the local REST server.
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

            NativeMenuItem dashboard = new NativeMenuItem("Launch Dashboard");
            dashboard.Click += OnLaunchDashboard;
            menu.Items.Add(dashboard);

            NativeMenuItem terminal = new NativeMenuItem("Launch Terminal");
            terminal.Click += OnLaunchTerminal;
            menu.Items.Add(terminal);

            NativeMenuItem desktop = new NativeMenuItem("Launch Desktop");
            desktop.Click += OnLaunchDesktop;
            menu.Items.Add(desktop);

            menu.Items.Add(new NativeMenuItemSeparator());

            NativeMenuItem about = new NativeMenuItem("About");
            about.Click += OnAbout;
            menu.Items.Add(about);

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
            // A single green glyph is used for the taskbar/notification icon on both light and dark taskbars,
            // matching the rest of the app. Load the full-resolution PNG (not the tiny .ico) so it renders
            // crisp and full-size rather than small and blurry.
            string resource = "Mux.Agent.logo-green.png";
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

        private void OnLaunchTerminal(object? sender, EventArgs e)
        {
            _Host?.LaunchTerminal();
        }

        private void OnLaunchDesktop(object? sender, EventArgs e)
        {
            _Host?.LaunchDesktop();
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
