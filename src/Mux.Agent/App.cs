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
            // The name macOS shows in the menu bar and app switcher while an agent window is open.
            Name = "mux agent";
            Styles.Add(new FluentTheme());

            // The macOS application menu must be installed before Avalonia creates its default one.
            if (OperatingSystem.IsMacOS())
            {
                NativeMenu appMenu = new NativeMenu();
                NativeMenuItem aboutItem = new NativeMenuItem("About mux agent");
                aboutItem.Click += OnAbout;
                appMenu.Items.Add(aboutItem);
                appMenu.Items.Add(new NativeMenuItemSeparator());
                NativeMenuItem dashboardItem = new NativeMenuItem("Launch Dashboard");
                dashboardItem.Click += OnLaunchDashboard;
                appMenu.Items.Add(dashboardItem);
                NativeMenuItem terminalItem = new NativeMenuItem("Launch Terminal");
                terminalItem.Click += OnLaunchTerminal;
                appMenu.Items.Add(terminalItem);
                NativeMenuItem desktopItem = new NativeMenuItem("Launch Desktop");
                desktopItem.Click += OnLaunchDesktop;
                appMenu.Items.Add(desktopItem);
                NativeMenu.SetMenu(this, appMenu);
            }
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

            // macOS menu-bar icons are template images: drawn from the glyph's alpha in the menu bar's own color,
            // so the "M" reads correctly on light and dark menu bars.
            if (OperatingSystem.IsMacOS())
            {
                MacOSProperties.SetIsTemplateIcon(_Tray, true);
            }
            _Tray.IsVisible = true;

            // Register the icon with the application. A TrayIcon that is only constructed is never added to the
            // macOS status bar; Avalonia creates the platform status item for icons attached here.
            TrayIcon.SetIcons(this, new TrayIcons { _Tray });

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
            // While a window is open the agent shows in the Dock (with the mux icon) so the window can be found
            // and focused; it returns to menu-bar-only when the window closes.
            if (OperatingSystem.IsMacOS())
            {
                MacActivationPolicy.ShowInDock();
                if (!MacDockIcon.IsRunningFromBundle())
                {
                    using (Stream? icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Mux.Agent.icon-macos.png"))
                    {
                        MacDockIcon.TryApply(icon);
                    }
                }
            }

            AboutWindow window = new AboutWindow();
            window.Closed += (closedSender, args) =>
            {
                if (OperatingSystem.IsMacOS())
                {
                    MacActivationPolicy.HideFromDock();
                }
            };
            window.Show();
            window.Activate();
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
