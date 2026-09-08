namespace Mux.Agent
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Server;

    /// <summary>
    /// Owns the background <see cref="MuxServer"/> for the tray agent and the "Launch Mux" action. The tray
    /// agent is itself the opt-in: launching it starts the server (loopback, token-guarded).
    /// </summary>
    public sealed class AgentHost
    {
        private MuxServer? _Server;

        /// <summary>
        /// The base URL the server is listening on, or an empty string before start.
        /// </summary>
        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Start the background REST server.
        /// </summary>
        public void Start()
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            RestServerSettings rest = settings.Rest;

            string? key = rest.ApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "mux_" + Guid.NewGuid().ToString("N");
                rest.ApiKey = key;
                try { SettingsLoader.SaveSettings(settings); } catch (Exception) { }
            }
            else
            {
                key = SettingsLoader.ExpandEnvironmentVariables(key);
            }
            rest.ApiKey = key;

            string sessionsDir = Path.Combine(SettingsLoader.GetConfigDirectory(), "sessions");
            _Server = new MuxServer(
                rest,
                Defaults.ProductVersion,
                new SessionStore(sessionsDir),
                () => SettingsLoader.LoadEndpoints(),
                logger: null);

            _Server.Start();
            BaseUrl = _Server.BaseUrl;
        }

        /// <summary>
        /// Launch the interactive mux TUI as an independent process.
        /// </summary>
        public void LaunchMux()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "mux", UseShellExecute = true });
                return;
            }
            catch (Exception)
            {
                // Fall through to a shell-mediated launch when "mux" is not directly spawnable.
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo { FileName = "cmd", Arguments = "/c mux", UseShellExecute = true });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "/bin/sh", Arguments = "-c mux", UseShellExecute = true });
                }
            }
            catch (Exception)
            {
                // Best effort; the tray stays up either way.
            }
        }

        /// <summary>
        /// Stop the background server.
        /// </summary>
        public void Stop()
        {
            try
            {
                _Server?.Stop();
                _Server?.Dispose();
            }
            catch (Exception)
            {
                // Best-effort shutdown.
            }
            _Server = null;
        }
    }
}
