namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Routes for web search settings: enable search, allow fallback, and add, edit, enable, delete, or choose the
    /// default provider. The same settings as the desktop Search Providers window. API keys are never returned.
    /// </summary>
    public sealed class SearchProviderRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private static readonly List<string> _ProviderTypes = new List<string> { "tavily", "you" };

        private readonly string? _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public SearchProviderRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/search", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(ToDto(SettingsLoader.LoadSettings().ExternalSearch)).ConfigureAwait(false);
            }, Documentation.ApiDoc.SearchGet);

            // Replace the whole search configuration. A provider sent with a blank ApiKey keeps the key stored under
            // the same name; providers left out are removed.
            app.Put("/v1.0/api/search", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                SearchSettingsDto? body;
                try
                {
                    body = JsonSerializer.Deserialize<SearchSettingsDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (JsonException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (body == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "A search settings object is required.");
                }

                string? invalid = Validate(body.Providers);
                if (invalid != null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", invalid);
                }

                try
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    settings.ExternalSearch = Apply(body, settings.ExternalSearch);
                    SettingsLoader.SaveSettings(settings);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(ToDto(SettingsLoader.LoadSettings().ExternalSearch)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return new ApiError("SaveFailed", "Failed to save search settings: " + ex.Message);
                }
            }, Documentation.ApiDoc.SearchPut);

            app.Delete("/v1.0/api/search/providers/{name}", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string name = Uri.UnescapeDataString(req.Parameters?["name"] ?? string.Empty);
                MuxSettings settings = SettingsLoader.LoadSettings();
                int removed = settings.ExternalSearch.Providers.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiError("NotFound", "No search provider is named '" + name + "'.");
                }

                try
                {
                    SettingsLoader.SaveSettings(settings);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(ToDto(SettingsLoader.LoadSettings().ExternalSearch)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return new ApiError("SaveFailed", "Failed to save search settings: " + ex.Message);
                }
            }, Documentation.ApiDoc.SearchProviderDelete);
        }

        #endregion

        #region Private-Methods

        private static SearchSettingsDto ToDto(ExternalSearchSettings settings)
        {
            SearchSettingsDto dto = new SearchSettingsDto
            {
                Enabled = settings.Enabled,
                AllowFallback = settings.AllowFallback,
                ProviderTypes = new List<string>(_ProviderTypes)
            };

            foreach (ExternalSearchProviderConfig provider in settings.Providers)
            {
                dto.Providers.Add(new SearchProviderDto
                {
                    Name = provider.Name,
                    ProviderType = provider.ProviderType,
                    Endpoint = provider.Endpoint,
                    ApiKeySet = !string.IsNullOrEmpty(provider.ApiKey),
                    ApiKey = null,
                    Enabled = provider.Enabled,
                    IsDefault = provider.IsDefault,
                    TimeoutMs = provider.TimeoutMs
                });
            }

            return dto;
        }

        private static ExternalSearchSettings Apply(SearchSettingsDto body, ExternalSearchSettings existing)
        {
            ExternalSearchSettings result = new ExternalSearchSettings { Enabled = body.Enabled, AllowFallback = body.AllowFallback };
            foreach (SearchProviderDto provider in body.Providers)
            {
                ExternalSearchProviderConfig? stored = existing.Providers.FirstOrDefault(p => string.Equals(p.Name, provider.Name, StringComparison.OrdinalIgnoreCase));
                ExternalSearchProviderConfig config = new ExternalSearchProviderConfig
                {
                    Name = provider.Name.Trim(),
                    ProviderType = string.IsNullOrWhiteSpace(provider.ProviderType) ? "tavily" : provider.ProviderType.Trim().ToLowerInvariant(),
                    Endpoint = provider.Endpoint ?? string.Empty,
                    ApiKey = string.IsNullOrWhiteSpace(provider.ApiKey) ? stored?.ApiKey ?? string.Empty : provider.ApiKey!.Trim(),
                    Enabled = provider.Enabled,
                    IsDefault = provider.IsDefault
                };

                if (provider.TimeoutMs > 0)
                {
                    config.TimeoutMs = provider.TimeoutMs;
                }

                result.Providers.Add(config);
            }

            return result;
        }

        private static string? Validate(List<SearchProviderDto>? providers)
        {
            if (providers == null)
            {
                return null;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SearchProviderDto provider in providers)
            {
                if (provider == null || string.IsNullOrWhiteSpace(provider.Name))
                {
                    return "Every provider needs a name.";
                }

                if (!names.Add(provider.Name.Trim()))
                {
                    return "Provider names must be unique ('" + provider.Name + "' repeats).";
                }

                string type = string.IsNullOrWhiteSpace(provider.ProviderType) ? "tavily" : provider.ProviderType.Trim().ToLowerInvariant();
                if (!_ProviderTypes.Contains(type))
                {
                    return "Unknown provider type '" + provider.ProviderType + "'. Use " + string.Join(" or ", _ProviderTypes) + ".";
                }
            }

            return null;
        }

        private static object Unauthorized(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        #endregion
    }
}
