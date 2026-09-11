namespace Mux.Desktop
{
    using System;
    using System.IO;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Controls.ApplicationLifetimes;
    using Avalonia.Styling;
    using Avalonia.Themes.Fluent;
    using Avalonia.Threading;
    using Microsoft.Extensions.DependencyInjection;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using Mux.Desktop.I18n;
    using Mux.Desktop.Services;
    using Mux.Desktop.Shell;
    using Mux.Desktop.Views;

    /// <summary>
    /// The Avalonia application for mux desktop. Applies the Fluent theme (light variant), composes the
    /// service container, shows a startup splash, then reveals the main window. Shutdown is tied to the main
    /// window so the splash closing does not end the process.
    /// </summary>
    public sealed class App : Application
    {
        /// <summary>How long the startup splash is shown before the main window appears.</summary>
        private static readonly TimeSpan SplashDuration = TimeSpan.FromSeconds(1.6);

        private IServiceProvider? _Services;
        private UsageTelemetry? _Telemetry;
        private SplashWindow? _Splash;
        private DispatcherTimer? _SplashTimer;

        /// <summary>
        /// Initialize application-level styles and the theme. The app uses the light variant for a clean,
        /// high-contrast surface; a full light/dark token system is a later phase.
        /// </summary>
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = AppTheme.Current.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        /// <summary>
        /// Complete framework initialization: build services, show the splash, and schedule the main window.
        /// </summary>
        public override void OnFrameworkInitializationCompleted()
        {
            // Bring up the tray agent (background REST server, telemetry, dashboard), like the interactive
            // TUI does. Best-effort and opt-out via MUX_AGENT_AUTOSTART=0.
            AgentLauncher.EnsureRunning();

            string configDirectory = ResolveConfigDirectory();
            _Services = BuildServices(configDirectory);

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                ILocalizationService localization = _Services.GetRequiredService<ILocalizationService>();
                IThreadService threads = _Services.GetRequiredService<IThreadService>();
                SessionStore store = _Services.GetRequiredService<SessionStore>();
                _Telemetry = _Services.GetRequiredService<UsageTelemetry>();
                UsageQueryService? usageQuery = _Telemetry.CreateQueryService(() => SettingsLoader.LoadPricing());

                MainWindow main = new MainWindow(localization, threads, store, _Telemetry.Recorder, usageQuery, configDirectory);
                desktop.MainWindow = main;

                // Closing the splash must not end the app; tie shutdown to the main window instead.
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.ShutdownRequested += (sender, args) => ShutdownServices();

                _Splash = new SplashWindow(localization);
                _Splash.Show();

                _SplashTimer = new DispatcherTimer { Interval = SplashDuration };
                _SplashTimer.Tick += (sender, args) => RevealMainWindow(main);
                _SplashTimer.Start();
            }

            base.OnFrameworkInitializationCompleted();
        }

        private void RevealMainWindow(MainWindow main)
        {
            if (main == null)
            {
                return;
            }

            _SplashTimer?.Stop();
            _SplashTimer = null;

            main.Show();
            main.Activate();

            _Splash?.Close();
            _Splash = null;
        }

        private void ShutdownServices()
        {
            try
            {
                _Telemetry?.Dispose(); // flush and close the usage store
            }
            catch (Exception)
            {
                // Best-effort shutdown.
            }

            _Telemetry = null;
        }

        private static IServiceProvider BuildServices(string configDirectory)
        {
            ServiceCollection services = new ServiceCollection();
            services.AddSingleton<ILocalizationService, LocalizationService>();
            services.AddSingleton(new SessionStore(Path.Combine(configDirectory, "sessions")));
            services.AddSingleton<IThreadService, ThreadService>();
            services.AddSingleton(UsageTelemetry.Create(SettingsLoader.LoadSettings(), configDirectory, null));
            return services.BuildServiceProvider();
        }

        private static string ResolveConfigDirectory()
        {
            try
            {
                return SettingsLoader.GetConfigDirectory();
            }
            catch (Exception)
            {
                return AppContext.BaseDirectory;
            }
        }
    }
}
