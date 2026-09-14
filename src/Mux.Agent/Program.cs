namespace Mux.Agent
{
    using System;
    using System.Diagnostics;
    using Avalonia;
    using Avalonia.Controls;
    using Mux.Core.Settings;
    using Mux.Core.Startup;

    /// <summary>
    /// Entry point for the mux tray agent. Starts an Avalonia application whose only surface is the
    /// system-tray icon; there is no main window, so the process keeps running until the tray's Exit action
    /// shuts it down.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the Avalonia lifetime.</param>
        /// <returns>The process exit code.</returns>
        public static int Main(string[] args)
        {
            string configDirectory = ResolveConfigDirectory();

            // Single-instance guard: only one tray agent may run at a time.
            AgentInstanceLock? instance = AgentInstanceLock.TryAcquire(configDirectory);
            if (instance == null)
            {
                Console.Error.WriteLine("Another mux agent is already running; exiting.");
                return 0;
            }

            // On first run, register the agent to start at login (idempotent, best-effort, opt-out via
            // MUX_AGENT_AUTOSTART=0). Doing this in the app means installers need no startup script — which
            // is why macOS ships a plain .dmg rather than a .pkg.
            TryRegisterStartup(configDirectory);

            try
            {
                using (instance)
                {
                    return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("mux agent error: " + ex.Message);
                return 2;
            }
        }

        private static void TryRegisterStartup(string configDirectory)
        {
            try
            {
                string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(exePath))
                {
                    StartupRegistrar.EnsureRegisteredOnFirstRun(exePath, configDirectory);
                }
            }
            catch (Exception)
            {
                // Never let startup registration prevent the agent from launching.
            }
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

        /// <summary>
        /// Build the Avalonia application.
        /// </summary>
        /// <returns>The configured application builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
        }
    }
}
