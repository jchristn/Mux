namespace Mux.Cli.App
{
    using System;
    using System.IO;
    using Mux.Core.Settings;

    /// <summary>
    /// Best-effort diagnostic log for cross-surface sync, used to trace why a terminal-produced turn does or
    /// does not reach the shared store and the hub. Appends one line per event to <c>&lt;config&gt;/mux-sync.log</c>.
    /// Enabled only when the <c>MUX_SYNC_LOG</c> environment variable is set (to anything non-empty), so it is
    /// silent for normal use. Never throws.
    /// </summary>
    public static class SyncDiagnostics
    {
        #region Private-Members

        private static readonly object _Sync = new object();
        private static readonly bool _Enabled =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MUX_SYNC_LOG"));
        private static string? _Path;

        #endregion

        #region Public-Methods

        /// <summary>Whether diagnostic logging is enabled for this process.</summary>
        public static bool Enabled
        {
            get => _Enabled;
        }

        /// <summary>Appends a timestamped line to the sync log when enabled. Best-effort; never throws.</summary>
        /// <param name="message">The line to append.</param>
        public static void Log(string message)
        {
            if (!_Enabled)
            {
                return;
            }

            try
            {
                string path = ResolvePath();
                string line = DateTime.UtcNow.ToString("HH:mm:ss.fff") + " [pid " + Environment.ProcessId + "] " + message + Environment.NewLine;
                lock (_Sync)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch (Exception)
            {
                // Diagnostics must never disrupt the app.
            }
        }

        #endregion

        #region Private-Methods

        private static string ResolvePath()
        {
            if (_Path != null)
            {
                return _Path;
            }

            string dir;
            try { dir = SettingsLoader.GetConfigDirectory(); }
            catch (Exception) { dir = Path.GetTempPath(); }
            _Path = Path.Combine(dir, "mux-sync.log");
            return _Path;
        }

        #endregion
    }
}
