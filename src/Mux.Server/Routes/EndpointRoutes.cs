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
                        AdapterType = AdapterKebab(endpoint.AdapterType),
                        BaseUrl = string.IsNullOrWhiteSpace(endpoint.BaseUrl) ? null : endpoint.BaseUrl,
                        Model = endpoint.Model,
                        IsDefault = endpoint.IsDefault
                    });
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<EndpointSummary>(items)).ConfigureAwait(false);
            });

            app.Get("/v1.0/api/endpoints/detail", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                List<EndpointDto> items = _EndpointsProvider().Select(ToDto).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<EndpointDto>(items)).ConfigureAwait(false);
            });

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
                    return (object)new ListResponse<EndpointDto>(saved);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", "Failed to save endpoints: " + ex.Message);
                }
            });

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
            });
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
                AdapterType = AdapterKebab(e.AdapterType),
                BaseUrl = e.BaseUrl ?? string.Empty,
                Model = e.Model ?? string.Empty,
                IsDefault = e.IsDefault,
                MaxTokens = e.MaxTokens,
                Temperature = e.Temperature,
                ContextWindow = e.ContextWindow,
                TimeoutMs = e.TimeoutMs,
                AutoApproveTools = e.AutoApproveTools,
                MaxAgentIterations = e.MaxAgentIterations,
                ShowThinking = e.ShowThinking,
                ReasoningEffort = effortLevel,
                ApiKeySet = !string.IsNullOrWhiteSpace(e.ApiKey),
                ApiKey = null,
                Region = e.Region,
                Project = e.Project,
                ApiVersion = e.ApiVersion
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
                MaxAgentIterations = dto.MaxAgentIterations,
                ShowThinking = dto.ShowThinking,
                Region = string.IsNullOrWhiteSpace(dto.Region) ? null : dto.Region,
                Project = string.IsNullOrWhiteSpace(dto.Project) ? null : dto.Project,
                ApiVersion = string.IsNullOrWhiteSpace(dto.ApiVersion) ? null : dto.ApiVersion,
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

        private static string AdapterKebab(AdapterTypeEnum adapter)
        {
            switch (adapter)
            {
                case AdapterTypeEnum.Ollama: return "ollama";
                case AdapterTypeEnum.OpenAi: return "openai";
                case AdapterTypeEnum.Vllm: return "vllm";
                case AdapterTypeEnum.OpenAiCompatible: return "openai-compatible";
                case AdapterTypeEnum.Anthropic: return "anthropic";
                case AdapterTypeEnum.Gemini: return "gemini";
                case AdapterTypeEnum.AzureOpenAi: return "azure-openai";
                case AdapterTypeEnum.Vertex: return "vertex";
                case AdapterTypeEnum.Bedrock: return "bedrock";
                default: return adapter.ToString().ToLowerInvariant();
            }
        }
    }
}
