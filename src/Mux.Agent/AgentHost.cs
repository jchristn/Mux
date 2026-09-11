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
        private Mux.Core.Telemetry.UsageTelemetry? _Telemetry;

        /// <summary>
        /// The base URL the server is listening on, or an empty string before start.
        /// </summary>
        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the background REST server is currently running.
        /// </summary>
        public bool IsRunning
        {
            get => _Server != null && !string.IsNullOrEmpty(BaseUrl);
        }

        /// <summary>
        /// Ensures the background REST server is running, starting it if it is not (for example when the
        /// initial start failed to bind). Idempotent and best-effort; never throws.
        /// </summary>
        public void EnsureStarted()
        {
            if (IsRunning)
            {
                return;
            }

            // Clear any half-started server before retrying so a new bind does not race a stale instance.
            Stop();
            try
            {
                Start();
            }
            catch (Exception)
            {
                // Best-effort; the tray stays up even if the server cannot bind.
            }
        }

        /// <summary>
        /// Ensures the server is serving (starting it if needed) and opens the web dashboard in the default
        /// browser. Best-effort; never throws.
        /// </summary>
        public void LaunchDashboard()
        {
            EnsureStarted();

            // Prefer the URL this agent bound. If binding failed because another process (a standalone
            // `mux serve`) already owns the port, the dashboard is still being served there — fall back to
            // the configured host/port so the browser opens the right place either way.
            string url = !string.IsNullOrEmpty(BaseUrl) ? BaseUrl : ConfiguredBaseUrl();
            if (!string.IsNullOrEmpty(url))
            {
                OpenUrl(url.TrimEnd('/') + "/dashboard");
            }
        }

        private static string ConfiguredBaseUrl()
        {
            try
            {
                RestServerSettings rest = SettingsLoader.LoadSettings().Rest;
                string scheme = rest.Ssl ? "https" : "http";
                return scheme + "://" + rest.Hostname + ":" + rest.Port;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Opens a URL in the operating system's default browser. Best-effort; never throws.
        /// </summary>
        /// <param name="url">The URL to open. Ignored when null or blank.</param>
        public static void OpenUrl(string url)
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

            // Open the shared usage-telemetry store and hand the server a query service (so the dashboard's
            // charts and KPIs are populated) and a recorder (so the agent's own chat calls are captured).
            // Without this the agent served an empty dashboard even though `mux serve` did not. Best-effort:
            // a disabled/unopenable store yields empty data and a no-op recorder.
            _Telemetry?.Dispose();
            _Telemetry = Mux.Core.Telemetry.UsageTelemetry.Create(settings, SettingsLoader.GetConfigDirectory(), null);

            _Server = new MuxServer(
                rest,
                Defaults.ProductVersion,
                new SessionStore(sessionsDir),
                () => SettingsLoader.LoadEndpoints(),
                logger: null,
                usageQuery: _Telemetry.CreateQueryService(() => SettingsLoader.LoadPricing()),
                usageRecorder: _Telemetry.Recorder);

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
    }
}
