namespace Mux.Desktop
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using Avalonia;
    using Mux.Core.Settings;

    /// <summary>
    /// Entry point for the mux desktop application. Acquires a single-instance lock, then starts the
    /// Avalonia classic desktop lifetime whose main surface is the <see cref="Shell.MainWindow"/>.
    /// </summary>
    public static class Program
    {
        private const int AttachParentProcess = -1;

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int dwProcessId);

        // The mux block-style wordmark, printed to the launching terminal at startup (like the TUI).
        private static readonly string[] Banner =
        {
            string.Empty,
            "▄▄   ▄▄ ▄▄ ▄▄ ▄▄ ▄▄",
            "██▀▄▀██ ██ ██ ▀█▄█▀",
            "██   ██ ▀███▀ ██ ██",
            string.Empty,
        };

        /// <summary>
        /// Program entry point.
        /// </summary>
        /// <param name="args">Command-line arguments passed to the Avalonia lifetime.</param>
        /// <returns>The process exit code: 0 on clean exit or when another instance already runs, 2 on error.</returns>
        public static int Main(string[] args)
        {
            PrintBanner();

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
                Console.WriteLine("mux desktop error:" + Environment.NewLine + ex.ToString());
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

        // Print the wordmark to the terminal that launched the app. The desktop app is a GUI (WinExe)
        // subsystem binary, so on Windows it is not attached to the parent console by default — attach to it
        // first. Entirely best-effort: it never blocks or fails startup, and is a no-op when there is no
        // console (e.g. launched from a shortcut).
        private static void PrintBanner()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    AttachConsole(AttachParentProcess);
                }

                try
                {
                    Console.OutputEncoding = Encoding.UTF8;
                }
                catch (Exception)
                {
                    // No console (or redirected output) — skip encoding and still try to write.
                }

                foreach (string line in Banner)
                {
                    Console.WriteLine(line);
                }

                Console.WriteLine(" mux desktop v" + Defaults.ProductVersion);
                Console.WriteLine();
            }
            catch (Exception)
            {
                // Best-effort banner; never let it interfere with launching the app.
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
    }
}
