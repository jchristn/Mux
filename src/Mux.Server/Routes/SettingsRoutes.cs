namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
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
            }, Documentation.ApiDoc.SettingsGet);

            app.Put("/v1.0/api/settings", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

                // A partial body changes only the fields it names: it is merged onto the current settings before the
                // DTO is applied, so a client that sends a subset never resets the rest to defaults.
                SettingsDto? dto;
                try
                {
                    string body = req.Http.Request.DataAsString ?? string.Empty;
                    dto = MergeOntoCurrent(body);
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

                    // The connect switch is credential-adjacent and written only through UpdateCloud (SaveSettings
                    // preserves the on-disk value so stale copies cannot flip it).
                    if (dto.Cloud != null)
                    {
                        bool enabled = dto.Cloud.Enabled;
                        settings.Cloud.Enabled = SettingsLoader.UpdateCloud(c => c.Enabled = enabled).Enabled;
                    }

                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(SettingsDto.FromSettings(settings)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", "Failed to save settings: " + ex.Message);
                }
            }, Documentation.ApiDoc.SettingsPut);
        }

        /// <summary>
        /// Merges a settings body onto the current settings: properties the body names (matched without regard to case,
        /// nested objects merged recursively) replace the current values; everything else keeps its current value.
        /// </summary>
        /// <param name="body">The request body (a JSON object).</param>
        /// <returns>The merged DTO, or null when the body is not a JSON object.</returns>
        /// <exception cref="JsonException">Thrown when the body is not valid JSON.</exception>
        internal static SettingsDto? MergeOntoCurrent(string body)
        {
            JsonNode? incoming = JsonNode.Parse(body);
            if (!(incoming is JsonObject incomingObject))
            {
                return null;
            }

            JsonObject current = JsonSerializer.SerializeToNode(SettingsDto.FromSettings(SettingsLoader.LoadSettings()))!.AsObject();
            Merge(current, incomingObject);
            return current.Deserialize<SettingsDto>(_JsonOptions);
        }

        private static void Merge(JsonObject target, JsonObject source)
        {
            foreach (KeyValuePair<string, JsonNode?> property in source.ToList())
            {
                string? key = null;
                foreach (KeyValuePair<string, JsonNode?> existing in target)
                {
                    if (string.Equals(existing.Key, property.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        key = existing.Key;
                        break;
                    }
                }

                key ??= property.Key;
                if (property.Value is JsonObject sourceChild && target[key] is JsonObject targetChild)
                {
                    Merge(targetChild, sourceChild);
                }
                else
                {
                    target[key] = property.Value?.DeepClone();
                }
            }
        }
    }
}
