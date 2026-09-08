namespace Mux.Agent
{
    using System;
    using Avalonia;
    using Avalonia.Controls;
    using Mux.Core.Settings;

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
