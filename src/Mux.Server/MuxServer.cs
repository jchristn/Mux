namespace Mux.Server
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Server.Models;
    using Mux.Server.Routes;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;

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
        private readonly DateTime _StartUtc = DateTime.UtcNow;
        private readonly CancellationTokenSource _TokenSource = new CancellationTokenSource();

        private Webserver? _App;
        private bool _Disposed = false;

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
        public MuxServer(
            RestServerSettings settings,
            string version,
            SessionStore sessionStore,
            Func<List<EndpointConfig>> endpointsProvider,
            Action<string>? logger = null)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Version = version ?? string.Empty;
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _Logger = logger;
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
            _App.WebSocket("/v1.0/ws", HandleWebSocketAsync);

            _App.Start(_TokenSource.Token);
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
            try { _App?.Dispose(); } catch (Exception) { }
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

            // OpenAPI document generation (Server.UseOpenApi) is a planned follow-up; the exact Watson 7.1.1
            // OpenAPI configuration surface is being finalized. The server is fully functional without it.
        }

        private void RegisterRoutes(Webserver app)
        {
            string? apiKey = _Settings.ApiKey;
            new HealthRoutes(_Version, _StartUtc).Register(app);
            new EndpointRoutes(apiKey, _EndpointsProvider).Register(app);
            new SessionRoutes(apiKey, _SessionStore).Register(app);
            new ChatRoutes(apiKey, _EndpointsProvider).Register(app);
            new SettingsRoutes(apiKey).Register(app);
            new McpRoutes(apiKey).Register(app);
            new ConfigRoutes(apiKey).Register(app);
            new SkillRoutes(apiKey).Register(app);
            new OverviewRoutes(apiKey, _EndpointsProvider, _SessionStore, _Version, _StartUtc).Register(app);
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

        private async Task HandleWebSocketAsync(HttpContextBase ctx, WebSocketSession session)
        {
            // Reuse the JSONL-style event envelope: the first frame announces the connection. A fuller
            // per-run event bridge (assistant_text / tool_call_* / run_completed) is a documented follow-up.
            string hello = "{\"eventType\":\"server.connected\",\"product\":\"mux\",\"version\":\"" + _Version + "\"}";
            await session.SendTextAsync(hello, ctx.Token).ConfigureAwait(false);

            await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token).ConfigureAwait(false))
            {
                if (message.MessageType == WebSocketMessageType.Text)
                {
                    await session.SendTextAsync("{\"eventType\":\"ack\"}", ctx.Token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
