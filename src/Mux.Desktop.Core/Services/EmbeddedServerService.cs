namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Telemetry;
    using Mux.Server;

    /// <summary>
    /// Runs the optional embedded REST + dashboard server (<c>mux serve</c>) in-process for the desktop app
    /// (§12 / row 42). It reuses the same wiring as <c>ServeCommand</c>/<c>AgentHost</c>: it binds
    /// <see cref="Mux.Server.MuxServer"/> to the configured loopback host/port, generates and persists a local
    /// API key when none is stored, opens the shared usage store so the dashboard has data, and records the
    /// server's own chat calls. It is off by default and never required — the desktop always drives the engine
    /// in-process. A process-wide singleton (<see cref="Instance"/>) keeps the server alive across window opens
    /// so toggling it from any view is consistent.
    /// </summary>
    public sealed class EmbeddedServerService
    {
        #region Private-Members

        private static readonly EmbeddedServerService _Instance = new EmbeddedServerService();
        private readonly object _Sync = new object();

        private MuxServer? _Server;
        private UsageTelemetry? _Telemetry;
        private string? _BaseUrl;
        private string? _ApiKey;
        private bool _AuthEnabled;
        private string? _LastError;

        #endregion

        #region Constructors-and-Factories

        private EmbeddedServerService()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>The process-wide embedded-server controller.</summary>
        public static EmbeddedServerService Instance
        {
            get => _Instance;
        }

        /// <summary>Whether the embedded server is currently listening.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_Sync)
                {
                    return _Server != null;
                }
            }
        }

        /// <summary>The base URL the server is bound to, or null when stopped.</summary>
        public string? BaseUrl
        {
            get
            {
                lock (_Sync)
                {
                    return _BaseUrl;
                }
            }
        }

        /// <summary>The dashboard URL, or null when stopped.</summary>
        public string? DashboardUrl
        {
            get
            {
                lock (_Sync)
                {
                    return _BaseUrl == null ? null : _BaseUrl + "/dashboard";
                }
            }
        }

        /// <summary>The health-check URL, or null when stopped.</summary>
        public string? HealthUrl
        {
            get
            {
                lock (_Sync)
                {
                    return _BaseUrl == null ? null : _BaseUrl + "/v1.0/api/health";
                }
            }
        }

        /// <summary>The effective local API key while running, or null when auth is disabled or stopped.</summary>
        public string? ApiKey
        {
            get
            {
                lock (_Sync)
                {
                    return _ApiKey;
                }
            }
        }

        /// <summary>Whether API-key auth is enforced for the running server.</summary>
        public bool AuthEnabled
        {
            get
            {
                lock (_Sync)
                {
                    return _AuthEnabled;
                }
            }
        }

        /// <summary>The reason the most recent <see cref="Start"/> failed, or null when it succeeded.</summary>
        public string? LastError
        {
            get
            {
                lock (_Sync)
                {
                    return _LastError;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the embedded server, binding the configured loopback host/port. A no-op when already running.
        /// Resolves settings fresh on each start so edits to the REST config take effect. On failure (for
        /// example a port already in use) the server is left stopped and <see cref="LastError"/> is set.
        /// </summary>
        /// <returns>True when the server is listening after the call; false when the start failed.</returns>
        public bool Start()
        {
            lock (_Sync)
            {
                if (_Server != null)
                {
                    return true;
                }

                _LastError = null;

                MuxServer? server = null;
                UsageTelemetry? telemetry = null;
                try
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    RestServerSettings rest = settings.Rest;

                    // Resolve the effective API key exactly as ServeCommand does: a stored key is expanded; a
                    // blank key is generated and persisted so the next run reuses it. Auth is always on here.
                    string apiKey;
                    string? stored = rest.ApiKey;
                    if (!string.IsNullOrWhiteSpace(stored))
                    {
                        apiKey = SettingsLoader.ExpandEnvironmentVariables(stored);
                    }
                    else
                    {
                        apiKey = "mux_" + Guid.NewGuid().ToString("N");
                        rest.ApiKey = apiKey;
                        try { SettingsLoader.SaveSettings(settings); } catch (Exception) { }
                    }

                    rest.ApiKey = apiKey;

                    string sessionsDir = Path.Combine(SettingsLoader.GetConfigDirectory(), "sessions");
                    SessionStore sessionStore = new SessionStore(sessionsDir);

                    telemetry = UsageTelemetry.Create(settings, SettingsLoader.GetConfigDirectory(), null);
                    UsageQueryService? usageQuery = telemetry.CreateQueryService(() => SettingsLoader.LoadPricing());

                    server = new MuxServer(
                        rest,
                        ProductVersion(),
                        sessionStore,
                        () => SettingsLoader.LoadEndpoints(),
                        logger: null,
                        usageQuery: usageQuery,
                        usageRecorder: telemetry.Recorder);

                    server.Start();

                    _Server = server;
                    _Telemetry = telemetry;
                    _BaseUrl = server.BaseUrl;
                    _ApiKey = apiKey;
                    _AuthEnabled = true;
                    return true;
                }
                catch (Exception ex)
                {
                    _LastError = ex.Message;
                    try { server?.Dispose(); } catch (Exception) { }
                    try { telemetry?.Dispose(); } catch (Exception) { }
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the embedded server and release its resources. A no-op when not running.
        /// </summary>
        public void Stop()
        {
            lock (_Sync)
            {
                try { _Server?.Dispose(); } catch (Exception) { }
                try { _Telemetry?.Dispose(); } catch (Exception) { }
                _Server = null;
                _Telemetry = null;
                _BaseUrl = null;
                _ApiKey = null;
                _AuthEnabled = false;
            }
        }

        /// <summary>
        /// Toggle the server: start it when stopped, stop it when running.
        /// </summary>
        /// <returns>True when the server is running after the call.</returns>
        public bool Toggle()
        {
            if (IsRunning)
            {
                Stop();
                return false;
            }

            return Start();
        }

        #endregion

        #region Private-Methods

        private static string ProductVersion()
        {
            try
            {
                System.Reflection.AssemblyInformationalVersionAttribute? attr =
                    Attribute.GetCustomAttribute(typeof(MuxServer).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute))
                        as System.Reflection.AssemblyInformationalVersionAttribute;
                if (attr != null && !string.IsNullOrWhiteSpace(attr.InformationalVersion))
                {
                    return attr.InformationalVersion;
                }
            }
            catch (Exception)
            {
                // Fall through to the assembly version.
            }

            return typeof(MuxServer).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        }

        #endregion
    }
}
