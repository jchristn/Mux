namespace Mux.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Read and edit the editable subset of mux settings. Secrets are masked on read; the REST API key is
    /// only changed when a caller supplies a new value.
    /// </summary>
    public sealed class SettingsRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string? _ApiKey;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public SettingsRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/settings", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

                MuxSettings settings = SettingsLoader.LoadSettings();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(SettingsDto.FromSettings(settings)).ConfigureAwait(false);
            });

            app.Put("/v1.0/api/settings", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

                SettingsDto? dto;
                try
                {
                    string body = req.Http.Request.DataAsString ?? string.Empty;
                    dto = JsonSerializer.Deserialize<SettingsDto>(body, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (dto == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A settings object is required.");
                }

                try
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    dto.ApplyTo(settings);
                    SettingsLoader.SaveSettings(settings);

                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(SettingsDto.FromSettings(settings)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", "Failed to save settings: " + ex.Message);
                }
            });
        }
    }
}
