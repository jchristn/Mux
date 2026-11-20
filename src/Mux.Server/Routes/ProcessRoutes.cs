namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Mux.Core.Processes;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Routes for the hub's background processes (the ones the model starts with <c>process_start</c>): list them,
    /// read recent output, stop one or all, and clear the exited ones. The same operations as the desktop and terminal
    /// <c>/processes</c> command.
    /// </summary>
    public sealed class ProcessRoutes
    {
        #region Private-Members

        private const int DefaultTailChars = 4000;
        private const int MaxTailChars = 200000;

        private readonly string? _ApiKey;
        private readonly BackgroundProcessRegistry _Registry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="registry">The hub's background process registry.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="registry"/> is null.</exception>
        public ProcessRoutes(string? apiKey, BackgroundProcessRegistry registry)
        {
            _ApiKey = apiKey;
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/processes", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<BackgroundProcessInfo>(new List<BackgroundProcessInfo>(_Registry.List()))).ConfigureAwait(false);
            }, Documentation.ApiDoc.ProcessesList);

            app.Get("/v1.0/api/processes/{id}/output", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                BackgroundProcessInfo? info = _Registry.Get(req.Parameters?["id"] ?? string.Empty);
                if (info == null) return NotFound(req.Http);

                int chars = DefaultTailChars;
                string? requested = req.Http.Request.Query.Elements["chars"];
                if (!string.IsNullOrWhiteSpace(requested) && int.TryParse(requested, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    chars = Math.Clamp(parsed, 1, MaxTailChars);
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ProcessOutputDto
                {
                    Id = info.Id,
                    Command = info.Command,
                    Text = _Registry.Tail(info.Id, chars) ?? string.Empty,
                    Running = info.Running,
                    ExitCode = info.ExitCode
                }).ConfigureAwait(false);
            }, Documentation.ApiDoc.ProcessesOutput);

            app.Post("/v1.0/api/processes/{id}/stop", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string id = req.Parameters?["id"] ?? string.Empty;
                if (string.Equals(id, "all", StringComparison.OrdinalIgnoreCase))
                {
                    int stopped = _Registry.StopAll();
                    req.Http.Response.StatusCode = 200;
                    return new { Stopped = stopped };
                }

                BackgroundProcessInfo? info = _Registry.Get(id);
                if (info == null) return NotFound(req.Http);

                try
                {
                    BackgroundProcessOutput output = await _Registry.StopAsync(info.Id, req.Http.Token).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 200;
                    return output;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    req.Http.Response.StatusCode = 500;
                    return new ApiError("StopFailed", "Could not stop " + info.Id + ": " + ex.Message);
                }
            }, Documentation.ApiDoc.ProcessesStop);

            app.Delete("/v1.0/api/processes", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                int removed = _Registry.RemoveExited();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new { Removed = removed }).ConfigureAwait(false);
            }, Documentation.ApiDoc.ProcessesClear);
        }

        #endregion

        #region Private-Methods

        private static object Unauthorized(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        private static object NotFound(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 404;
            return new ApiError("NotFound", "No background process has that id.");
        }

        #endregion
    }
}
