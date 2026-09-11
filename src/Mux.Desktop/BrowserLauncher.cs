namespace Mux.Desktop
{
    using System;
    using System.Diagnostics;

    /// <summary>
    /// Opens URLs in the operating system's default browser. Best-effort and cross-platform; never throws.
    /// </summary>
    internal static class BrowserLauncher
    {
        /// <summary>
        /// Open a URL in the default browser.
        /// </summary>
        /// <param name="url">The URL to open. Ignored when null or blank.</param>
        internal static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo { FileName = "open", Arguments = url, UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = url, UseShellExecute = false });
                }
            }
            catch (Exception)
            {
                // Best-effort; nothing to do if no browser handler is available.
            }
        }
    }
}
