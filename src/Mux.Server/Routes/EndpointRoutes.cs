namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Routes over the configured endpoints: a lightweight summary list for the chat picker, a full detail
    /// list for the editor (secrets masked), and create/update/delete backed by
    /// <see cref="SettingsLoader.SaveEndpoints"/>. Secret values (the API key and header values) are never
    /// sent to the client, and a blank secret on write preserves the stored value.
    /// </summary>
    public sealed class EndpointRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="endpointsProvider">Callback returning the configured endpoints.</param>
        public EndpointRoutes(string? apiKey, Func<List<EndpointConfig>> endpointsProvider)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/endpoints", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                List<EndpointSummary> items = new List<EndpointSummary>();
                foreach (EndpointConfig endpoint in _EndpointsProvider())
                {
                    items.Add(new EndpointSummary
                    {
                        Name = endpoint.Name,
                        AdapterType = endpoint.AdapterType.ToKebab(),
                        BaseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? null : endpoint.BaseUrl,
                        Model = endpoint.Model,
                        IsDefault = endpoint.IsDefault
                    });
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<EndpointSummary>(items)).ConfigureAwait(false);
            }, Documentation.ApiDoc.EndpointsList);

            app.Get("/v1.0/api/endpoints/detail", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                List<EndpointDto> items = _EndpointsProvider().Select(ToDto).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<EndpointDto>(items)).ConfigureAwait(false);
            }, Documentation.ApiDoc.EndpointsDetail);

            // Save the whole endpoint collection (add/edit/remove client-side, then PUT the full list).
            app.Put("/v1.0/api/endpoints", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                ListResponse<EndpointDto>? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<ListResponse<EndpointDto>>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (payload?.Items == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "An 'items' array of endpoints is required.");
                }

                try
                {
                    Dictionary<string, EndpointConfig> existing = _EndpointsProvider()
                        .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    List<EndpointConfig> merged = new List<EndpointConfig>();
                    foreach (EndpointDto dto in payload.Items)
                    {
                        if (string.IsNullOrWhiteSpace(dto.Name))
                        {
                            req.Http.Response.StatusCode = 400;
                            return (object)new ApiError("BadRequest", "Every endpoint needs a name.");
                        }

                        existing.TryGetValue(dto.Name, out EndpointConfig? prior);
                        merged.Add(FromDto(dto, prior));
                    }

                    SettingsLoader.SaveEndpoints(merged);
                    List<EndpointDto> saved = SettingsLoader.LoadEndpoints().Select(ToDto).ToList();
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<EndpointDto>(saved)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", "Failed to save endpoints: " + ex.Message);
                }
            }, Documentation.ApiDoc.EndpointsPut);

            app.Delete("/v1.0/api/endpoints", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                string? name = req.Http.Request.Query.Elements["name"];
                if (string.IsNullOrWhiteSpace(name))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A 'name' query parameter is required.");
                }

                try
                {
                    List<EndpointConfig> remaining = _EndpointsProvider()
                        .Where(e => !string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    SettingsLoader.SaveEndpoints(remaining);
                    List<EndpointDto> saved = SettingsLoader.LoadEndpoints().Select(ToDto).ToList();
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<EndpointDto>(saved)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("DeleteFailed", "Failed to delete endpoint: " + ex.Message);
                }
            }, Documentation.ApiDoc.EndpointsDelete);

            app.Get("/v1.0/api/endpoints/models", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                string? name = req.Http.Request.Query.Elements["name"];
                if (string.IsNullOrWhiteSpace(name))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A 'name' query parameter is required.");
                }

                EndpointConfig? endpoint = _EndpointsProvider()
                    .FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
                if (endpoint == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return (object)new ApiError("NotFound", "No endpoint named '" + name + "'.");
                }

                // Asks the endpoint's backend which models its credentials can use (OpenAI, Anthropic, Gemini,
                // Bedrock, Azure OpenAI, xAI, Ollama, Ollama Cloud, and OpenAI-compatible model lists; Vertex has
                // no listing API and reports models_not_supported).
                using System.Threading.CancellationTokenSource cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                Mux.Core.Utility.EndpointModelListResult result = await Mux.Core.Utility.EndpointModelLister
                    .ListModelsAsync(endpoint, SettingsLoader.LoadSettings().IgnoreCertErrors, cts.Token)
                    .ConfigureAwait(false);

                req.Http.Response.StatusCode = 200;
                return new EndpointModelsDto
                {
                    Name = endpoint.Name,
                    AdapterType = endpoint.AdapterType.ToKebab(),
                    ConfiguredModel = endpoint.Model,
                    Success = result.Success,
                    Models = result.Models,
                    ErrorCode = string.IsNullOrEmpty(result.ErrorCode) ? null : result.ErrorCode,
                    ErrorMessage = string.IsNullOrEmpty(result.ErrorMessage) ? null : result.ErrorMessage
                };
            }, Documentation.ApiDoc.EndpointsModels);

            app.Post("/v1.0/api/endpoints/import", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                EndpointImportRequest? body;
                try
                {
                    body = JsonSerializer.Deserialize<EndpointImportRequest>(req.Http.Request.DataAsString ?? string.Empty, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (Exception)
                {
                    body = null;
                }

                if (body == null || string.IsNullOrWhiteSpace(body.Source) || body.Models == null || body.Models.Count == 0)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "'Source' (an endpoint name) and a non-empty 'Models' list are required.");
                }

                try
                {
                    // Cloning happens here, not in the browser, so the copies keep the source endpoint's stored
                    // API key and headers (the detail listing masks them).
                    List<EndpointConfig> all = SettingsLoader.LoadEndpoints();
                    EndpointConfig? source = all.FirstOrDefault(e => string.Equals(e.Name, body.Source, StringComparison.OrdinalIgnoreCase));
                    if (source == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return (object)new ApiError("NotFound", "No endpoint named '" + body.Source + "'.");
                    }

                    List<EndpointConfig> created = Mux.Core.Utility.EndpointModelImport.CreateEndpoints(source, body.Models, all);
                    all.AddRange(created);
                    SettingsLoader.SaveEndpoints(all);
                    List<EndpointDto> saved = SettingsLoader.LoadEndpoints().Select(ToDto).ToList();
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<EndpointDto>(saved)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", "Failed to import models: " + ex.Message);
                }
            }, Documentation.ApiDoc.EndpointsImport);
        }

        private object Unauthorized()
        {
            return new ApiError("Unauthorized", "Authentication required.");
        }

        private static EndpointDto ToDto(EndpointConfig e)
        {
            // Guard every dereference so the nullable analyzer never flags the chain (a bare ToString()
            // chain trips CS8602 under pack, where the project's NoWarn is not applied).
            string? effortLevel = e.ReasoningEffort?.Level.ToString()?.ToLowerInvariant();

            EndpointDto dto = new EndpointDto
            {
                Name = e.Name,
                AdapterType = e.AdapterType.ToKebab(),
                BaseUrl = e.BaseUrl ?? string.Empty,
                Model = e.Model ?? string.Empty,
                IsDefault = e.IsDefault,
                MaxTokens = e.MaxTokens,
                Temperature = e.Temperature,
                ContextWindow = e.ContextWindow,
                TimeoutMs = e.TimeoutMs,
                AutoApproveTools = e.AutoApproveTools,
                ToolAutoApproveSeconds = e.ToolAutoApproveSeconds,
                MaxAgentIterations = e.MaxAgentIterations,
                ShowThinking = e.ShowThinking,
                ReasoningEffort = effortLevel,
                ApiKeySet = !string.IsNullOrWhiteSpace(e.ApiKey),
                ApiKey = null,
                Region = e.Region,
                Project = e.Project,
                ApiVersion = e.ApiVersion,
                AuthPlacement = AuthPlacementEnumConverter.ToWire(e.AuthPlacement),
                AuthParameterName = e.AuthParameterName
            };

            if (e.Headers != null)
            {
                foreach (KeyValuePair<string, string> header in e.Headers)
                {
                    dto.Headers.Add(new HeaderDto { Key = header.Key, Value = string.Empty, ValueSet = !string.IsNullOrEmpty(header.Value) });
                }
            }

            return dto;
        }

        private static EndpointConfig FromDto(EndpointDto dto, EndpointConfig? prior)
        {
            AdapterTypeEnumConverter.TryParse(dto.AdapterType, out AdapterTypeEnum adapter);

            EndpointConfig config = new EndpointConfig
            {
                Name = dto.Name.Trim(),
                AdapterType = adapter,
                BaseUrl = dto.BaseUrl ?? string.Empty,
                Model = dto.Model ?? string.Empty,
                IsDefault = dto.IsDefault,
                MaxTokens = dto.MaxTokens,
                Temperature = dto.Temperature,
                ContextWindow = dto.ContextWindow,
                TimeoutMs = dto.TimeoutMs,
                AutoApproveTools = dto.AutoApproveTools,
                ToolAutoApproveSeconds = dto.ToolAutoApproveSeconds,
                MaxAgentIterations = dto.MaxAgentIterations,
                ShowThinking = dto.ShowThinking,
                Region = string.IsNullOrWhiteSpace(dto.Region) ? null : dto.Region,
                Project = string.IsNullOrWhiteSpace(dto.Project) ? null : dto.Project,
                ApiVersion = string.IsNullOrWhiteSpace(dto.ApiVersion) ? null : dto.ApiVersion,
                AuthPlacement = AuthPlacementEnumConverter.Parse(dto.AuthPlacement),
                AuthParameterName = string.IsNullOrWhiteSpace(dto.AuthParameterName) ? null : dto.AuthParameterName.Trim(),
                Headers = new Dictionary<string, string>(),
                // Reasoning effort is not edited from the dashboard yet; preserve whatever was configured.
                ReasoningEffort = prior?.ReasoningEffort?.Clone(),
                Quirks = prior?.Quirks
            };

            // API key: a blank incoming value preserves the stored key.
            config.ApiKey = string.IsNullOrWhiteSpace(dto.ApiKey) ? prior?.ApiKey : dto.ApiKey;

            // Headers: a blank value preserves the prior value for that key; a set value overwrites.
            foreach (HeaderDto header in dto.Headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    continue;
                }

                string key = header.Key.Trim();
                if (!string.IsNullOrEmpty(header.Value))
                {
                    config.Headers[key] = header.Value;
                }
                else if (prior?.Headers != null && prior.Headers.TryGetValue(key, out string? priorValue))
                {
                    config.Headers[key] = priorValue;
                }
                else
                {
                    config.Headers[key] = string.Empty;
                }
            }

            return config;
        }

    }

    /// <summary>
    /// The models an endpoint's backend advertises, from <c>GET /v1.0/api/endpoints/models</c>.
    /// </summary>
    public sealed class EndpointModelsDto
    {
        /// <summary>
        /// The endpoint name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The endpoint's adapter (kebab-case).
        /// </summary>
        public string AdapterType { get; set; } = string.Empty;

        /// <summary>
        /// The model the endpoint is configured with.
        /// </summary>
        public string ConfiguredModel { get; set; } = string.Empty;

        /// <summary>
        /// True when the listing succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The advertised model ids.
        /// </summary>
        public List<string> Models { get; set; } = new List<string>();

        /// <summary>
        /// A machine-readable failure code (for example <c>models_not_supported</c>), or null.
        /// </summary>
        public string? ErrorCode { get; set; }

        /// <summary>
        /// A human-readable failure message, or null.
        /// </summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Body of <c>POST /v1.0/api/endpoints/import</c>.
    /// </summary>
    public sealed class EndpointImportRequest
    {
        /// <summary>
        /// The endpoint whose settings the new endpoints copy.
        /// </summary>
        public string? Source { get; set; }

        /// <summary>
        /// The model ids to add, one endpoint each (already-configured models are skipped).
        /// </summary>
        public List<string>? Models { get; set; }
    }
}
