namespace Mux.Server
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Runs;
    using Mux.Core.Sessions;
    using Mux.Server.Models;
    using Mux.Server.Routes;
    using Mux.Server.Runs;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Hosts mux's optional local REST + WebSocket API on Watson 7. The server is opt-in and binds to
    /// loopback by default. Routes call mux's existing in-process services directly; there is no database
    /// layer. Follows the Watson 7 hosting rules in <c>agents/requirements/BACKEND_ARCHITECTURE.md</c>
    /// (registrar classes, Preflight/PostRouting, OpenAPI, 127.0.0.1), with the single-user deviations
    /// documented in <c>docs/REST_API.md</c>.
    /// </summary>
    public sealed class MuxServer : IDisposable
    {
        #region Private-Members

        private const string _Header = "[MuxServer] ";

        private readonly RestServerSettings _Settings;
        private readonly string _Version;
        private readonly SessionStore _SessionStore;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;
        private readonly Action<string>? _Logger;
        private readonly Mux.Core.Telemetry.UsageQueryService? _UsageQuery;
        private readonly Mux.Core.Telemetry.IUsageRecorder? _UsageRecorder;
        private readonly DateTime _StartUtc = DateTime.UtcNow;
        private readonly CancellationTokenSource _TokenSource = new CancellationTokenSource();

        private Webserver? _App;
        private bool _Disposed = false;
        private readonly bool _AllowInteractiveTools;
        private readonly CheckpointRegistry _Checkpoints = new CheckpointRegistry();
        private readonly RunRegistry _Runs;
        private readonly bool _OwnsRuns;
        private SessionStoreWatcher? _StoreWatcher;

        #endregion

        #region Public-Members

        /// <summary>
        /// The resolved base URL the server listens on.
        /// </summary>
        public string BaseUrl
        {
            get
            {
                string scheme = _Settings.Ssl ? "https" : "http";
                return scheme + "://" + _Settings.Hostname + ":" + _Settings.Port;
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the server host.
        /// </summary>
        /// <param name="settings">REST server settings.</param>
        /// <param name="version">Product version reported by the health route.</param>
        /// <param name="sessionStore">Session store to read persisted sessions from.</param>
        /// <param name="endpointsProvider">Callback returning the configured endpoints.</param>
        /// <param name="logger">Optional log sink for Watson events.</param>
        /// <param name="usageQuery">Optional usage-telemetry query service backing the dashboard's usage
        /// pages. Null disables the query endpoints (they return empty results with an enabled=false signal).</param>
        /// <param name="usageRecorder">Optional usage recorder so the server's own chat calls are captured.
        /// Null skips recording server-side calls.</param>
        /// <param name="allowInteractiveTools">When true, mutating tools proposed during a dashboard chat
        /// prompt the browser for approval instead of being auto-denied. Defaults to false.</param>
        /// <param name="runs">An externally-owned run registry to share (so a host process such as the desktop
        /// app can register its in-process runs and have them mirrored over this server's WebSocket bridge).
        /// When null, the server creates and owns its own registry. An injected registry is not disposed by
        /// the server.</param>
        public MuxServer(
            RestServerSettings settings,
            string version,
            SessionStore sessionStore,
            Func<List<EndpointConfig>> endpointsProvider,
            Action<string>? logger = null,
            Mux.Core.Telemetry.UsageQueryService? usageQuery = null,
            Mux.Core.Telemetry.IUsageRecorder? usageRecorder = null,
            bool allowInteractiveTools = false,
            RunRegistry? runs = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Version = version ?? string.Empty;
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _Logger = logger;
            _UsageQuery = usageQuery;
            _UsageRecorder = usageRecorder;
            _AllowInteractiveTools = allowInteractiveTools;
            _Runs = runs ?? new RunRegistry();
            _OwnsRuns = runs == null;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the server. Binds the listener and returns.
        /// </summary>
        public void Start()
        {
            WebserverSettings wsSettings = new WebserverSettings();
            wsSettings.Hostname = _Settings.Hostname;
            wsSettings.Port = _Settings.Port;
            wsSettings.Ssl.Enable = _Settings.Ssl;
            wsSettings.WebSockets.Enable = true;

            _App = new Webserver(wsSettings, DefaultRouteAsync);
            if (_Logger != null) _App.Events.Logger = _Logger;

            ConfigureServer(_App);
            RegisterRoutes(_App);
            WebSocketBridge bridge = new WebSocketBridge(_Settings.ApiKey, _Runs, _Version);
            _App.WebSocket("/v1.0/ws", bridge.HandleAsync);

            _App.Start(_TokenSource.Token);

            // Watch the shared session store so a turn written directly to disk by ANY process — an in-process
            // terminal or desktop run, or a second server — is rebroadcast to this server's WebSocket clients
            // (the dashboard and the VS Code extension). Without this, a thin client only learns of turns this
            // particular server persisted; with it, every store change reaches every connected surface,
            // independent of which hub or run produced it.
            SessionStoreWatcher watcher = new SessionStoreWatcher(_SessionStore.RootDirectory);
            watcher.Changed += id =>
            {
                _Runs.NotifyTranscriptChanged(id);
                _Runs.NotifySessionsChanged(id);
            };
            watcher.Removed += id => _Runs.NotifySessionsChanged(id);
            watcher.Start();
            _StoreWatcher = watcher;

            _Logger?.Invoke(_Header + "listening on " + BaseUrl);
        }

        /// <summary>
        /// Stop the server.
        /// </summary>
        public void Stop()
        {
            try
            {
                _TokenSource.Cancel();
                if (_App != null && _App.IsListening) _App.Stop();
            }
            catch (Exception)
            {
                // Best-effort shutdown.
            }
        }

        /// <summary>
        /// Release resources.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            Stop();
            try { _StoreWatcher?.Dispose(); } catch (Exception) { }
            try { _App?.Dispose(); } catch (Exception) { }
            if (_OwnsRuns) { try { _Runs.Dispose(); } catch (Exception) { } }
            try { _TokenSource.Dispose(); } catch (Exception) { }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private void ConfigureServer(Webserver app)
        {
            app.Routes.PreRouting = async (HttpContextBase ctx) =>
            {
                ctx.Timestamp.Start = DateTime.UtcNow;
                ctx.Response.ContentType = "application/json";
                await Task.CompletedTask.ConfigureAwait(false);
            };

            app.Routes.PostRouting = async (HttpContextBase ctx) =>
            {
                ctx.Timestamp.End = DateTime.UtcNow;
                _Logger?.Invoke(
                    _Header + ctx.Request.Method + " " + ctx.Request.Url.RawWithQuery + " " +
                    ctx.Response.StatusCode);
                ApplyCors(ctx);
                await Task.CompletedTask.ConfigureAwait(false);
            };

            app.Routes.Preflight = async (HttpContextBase ctx) =>
            {
                ApplyCors(ctx);
                ctx.Response.StatusCode = 200;
                await ctx.Response.Send().ConfigureAwait(false);
            };

            // Publish the OpenAPI 3.0 document at /openapi.json and the Swagger UI at /swagger. Both are
            // registered in Watson's PreAuthentication group and carry no per-handler API-key check, so the
            // machine-readable docs and the UI are reachable without a key. Every documented operation still
            // advertises the bearer requirement so generated clients attach the key. See ApiDoc for the full
            // info block, tag groups, security scheme, component schemas, and per-route metadata.
            Documentation.ApiDoc.Configure(app, _Version);
        }

        private void RegisterRoutes(Webserver app)
        {
            string? apiKey = _Settings.ApiKey;
            new HealthRoutes(_Version, _StartUtc).Register(app);
            new EndpointRoutes(apiKey, _EndpointsProvider).Register(app);
            new SessionRoutes(apiKey, _SessionStore, _Runs).Register(app);
            new ChatRoutes(apiKey, _EndpointsProvider, _UsageRecorder, _SessionStore, _AllowInteractiveTools, _Checkpoints, _Runs).Register(app);
            new CheckpointRoutes(apiKey, _Checkpoints).Register(app);
            new SettingsRoutes(apiKey).Register(app);
            new McpRoutes(apiKey).Register(app);
            new ConfigRoutes(apiKey).Register(app);
            new SkillRoutes(apiKey).Register(app);
            new OverviewRoutes(apiKey, _EndpointsProvider, _SessionStore, _Version, _StartUtc).Register(app);
            new UsageRoutes(apiKey, _UsageQuery).Register(app);
            new RunRoutes(apiKey, _Runs).Register(app);
        }

        private void ApplyCors(HttpContextBase ctx)
        {
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", _Settings.CorsAllowOrigin);
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, PUT, DELETE, OPTIONS, HEAD");
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
        }

        private async Task DefaultRouteAsync(HttpContextBase ctx)
        {
            string path = ctx.Request.Url.RawWithoutQuery ?? string.Empty;

            // Serve the self-contained single-page dashboard for /dashboard and its client-side routes. The
            // page is served over loopback with the API key injected so it can call the API on the user's
            // behalf. This is a low-level (raw HTML) response, not a JSON route.
            if (path == "/" || path == "/dashboard" || path == "/dashboard/" || path.StartsWith("/dashboard/", StringComparison.OrdinalIgnoreCase))
            {
                ApplyCors(ctx);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/html; charset=utf-8";
                await ctx.Response.Send(DashboardPage.Render(_Settings.ApiKey, _Version)).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send("{\"error\":\"NotFound\",\"message\":\"No matching route.\"}").ConfigureAwait(false);
        }

        #endregion
    }
}
