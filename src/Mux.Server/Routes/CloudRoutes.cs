namespace Mux.Server.Routes
{
    using System;
    using System.Text.Json;
    using Mux.Core.Cloud;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Mux Cloud connection routes on the local hub: read the tunnel status, sign this machine in (exchanging an email
    /// and password for a machine credential), and sign it out. Every surface (dashboard, VS Code, desktop, terminal)
    /// uses the same routes or the same <see cref="CloudAccountService"/>.
    /// </summary>
    public sealed class CloudRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly string? _ApiKey;
        private readonly MuxServer _Server;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="server">The hub whose tunnel status is reported.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public CloudRoutes(string? apiKey, MuxServer server)
        {
            _ApiKey = apiKey;
            _Server = server ?? throw new ArgumentNullException(nameof(server));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/cloud/status", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");
                req.Http.Response.StatusCode = 200;
                return await System.Threading.Tasks.Task.FromResult<object>(CurrentStatus()).ConfigureAwait(false);
            }, Documentation.ApiDoc.CloudStatusGet);

            app.Post("/v1.0/api/cloud/login", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");
                CloudLoginRequestDto? body;
                try
                {
                    body = JsonSerializer.Deserialize<CloudLoginRequestDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (JsonException)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (body == null || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrEmpty(body.Password))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "'email' and 'password' are required.");
                }

                try
                {
                    await CloudAccountService.LoginAsync(body.Email, body.Password, body.ServerUrl, body.MachineName, req.Http.Token).ConfigureAwait(false);
                }
                catch (ArgumentException e)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", e.Message);
                }
                catch (CloudEnrollmentException e)
                {
                    req.Http.Response.StatusCode = e.StatusCode == 401 ? 401 : e.StatusCode == 0 ? 502 : e.StatusCode >= 500 ? 502 : 400;
                    return (object)new ApiError(string.IsNullOrEmpty(e.ErrorCode) ? "CloudError" : e.ErrorCode, e.Message);
                }

                _Server.CloudHost?.Refresh();
                req.Http.Response.StatusCode = 200;
                return (object)CurrentStatus();
            }, Documentation.ApiDoc.CloudLogin);

            app.Post("/v1.0/api/cloud/logout", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");
                CloudAccountService.Logout();
                _Server.CloudHost?.Refresh();
                req.Http.Response.StatusCode = 200;
                return await System.Threading.Tasks.Task.FromResult<object>(CurrentStatus()).ConfigureAwait(false);
            }, Documentation.ApiDoc.CloudLogout);
        }

        #endregion

        #region Private-Methods

        private CloudStatus CurrentStatus()
        {
            if (_Server.CloudHost != null) return _Server.CloudHost.Status;
            MuxSettings settings = SettingsLoader.LoadSettings();
            return CloudAccountService.OfflineStatus(settings.Cloud, settings.Cloud.IsEnrolled && settings.Cloud.Enabled ? "This mux process is not running the Mux Cloud tunnel." : null);
        }

        #endregion
    }
}
