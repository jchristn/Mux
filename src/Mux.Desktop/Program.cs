namespace Mux.Desktop
{
    using System;
    using Avalonia;
    using Mux.Core.Settings;

    /// <summary>
    /// Entry point for the mux desktop application. Acquires a single-instance lock, then starts the
    /// Avalonia classic desktop lifetime whose main surface is the <see cref="Shell.MainWindow"/>.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the Avalonia lifetime.</param>
        /// <returns>The process exit code: 0 on clean exit or when another instance already runs, 2 on error.</returns>
        public static int Main(string[] args)
        {
            string configDirectory = ResolveConfigDirectory();

            // Single-instance guard: only one desktop instance may run per config directory.
            DesktopInstanceLock? instance = DesktopInstanceLock.TryAcquire(configDirectory);
            if (instance == null)
            {
                Console.Error.WriteLine("Another mux desktop instance is already running; exiting.");
                return 0;
            }

            try
            {
                using (instance)
                {
                    return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("mux desktop error: " + ex.Message);
                return 2;
            }
        }

        /// <summary>
        /// Build the Avalonia application. Exposed so design-time tooling can discover the app builder.
        /// </summary>
        /// <returns>The configured application builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
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
