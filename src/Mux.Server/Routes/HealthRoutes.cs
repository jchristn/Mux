namespace Mux.Server.Routes
{
    using System;
    using System.Diagnostics;
    using System.Threading.Tasks;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Anonymous health/status route.
    /// </summary>
    public sealed class HealthRoutes
    {
        private readonly string _Version;
        private readonly DateTime _StartUtc;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="version">Product version.</param>
        /// <param name="startUtc">Server start time.</param>
        public HealthRoutes(string version, DateTime startUtc)
        {
            _Version = version ?? string.Empty;
            _StartUtc = startUtc;
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/health", async (req) =>
            {
                TimeSpan uptime = DateTime.UtcNow - _StartUtc;
                HealthResponse response = new HealthResponse
                {
                    Version = _Version,
                    Pid = Process.GetCurrentProcess().Id,
                    StartedUtc = _StartUtc,
                    Uptime = uptime.ToString(@"d\.hh\:mm\:ss"),
                    TimestampUtc = DateTime.UtcNow
                };
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(response).ConfigureAwait(false);
            });
        }
    }
}
