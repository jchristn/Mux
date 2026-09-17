namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Routes for the operational-prompt catalog: enumerate every catalog entry with its default, effective
    /// value, and overridden flag, and set or clear a single global-scoped override. Profile-scoped persona
    /// prompts are edited through the prompt-profile routes (<c>/v1.0/api/prompts</c>); this surface covers the
    /// global operational prompts stored in the <c>operational</c> map of <c>prompts.json</c>.
    /// </summary>
    public sealed class PromptCatalogRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public PromptCatalogRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/prompts/catalog", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                PromptResolver resolver = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
                List<PromptCatalogEntryDto> items = PromptCatalog.All.Select(definition => ToDto(definition, resolver)).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<PromptCatalogEntryDto>(items)).ConfigureAwait(false);
            }, Documentation.ApiDoc.PromptCatalogGet);

            app.Put("/v1.0/api/prompts/catalog", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();

                PromptOverrideDto? payload = Parse<PromptOverrideDto>(req.Http.Request.DataAsString);
                if (payload == null || string.IsNullOrWhiteSpace(payload.Key))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A 'key' is required.");
                }

                if (!PromptCatalog.TryGet(payload.Key, out PromptDefinition? definition) || definition == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Unknown prompt key: " + payload.Key);
                }

                if (definition!.Scope != PromptScope.Global)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Prompt '" + payload.Key + "' is " + definition.Scope + "-scoped and has no operational override.");
                }

                try
                {
                    if (string.IsNullOrWhiteSpace(payload.Content))
                    {
                        PromptResolver.ResetToDefault(payload.Key);
                    }
                    else
                    {
                        PromptResolver.SetOverride(payload.Key, payload.Content!);
                    }
                }
                catch (ArgumentException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", ex.Message);
                }

                PromptResolver resolver = new PromptResolver(SettingsLoader.LoadOperationalPrompts());
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(ToDto(definition, resolver)).ConfigureAwait(false);
            }, Documentation.ApiDoc.PromptCatalogPut);
        }

        private object Unauthorized() => new ApiError("Unauthorized", "Authentication required.");

        private static PromptCatalogEntryDto ToDto(PromptDefinition definition, PromptResolver resolver)
        {
            return new PromptCatalogEntryDto
            {
                Key = definition.Key,
                Kind = definition.Kind.ToWireString(),
                Scope = definition.Scope.ToString(),
                DisplayName = definition.DisplayName,
                Description = definition.Description,
                Placeholders = new List<string>(definition.Placeholders),
                Default = definition.DefaultContent,
                Effective = resolver.GetEffective(definition.Key),
                Overridden = resolver.IsOverridden(definition.Key),
                Editable = definition.Scope == PromptScope.Global
            };
        }

        private T? Parse<T>(string body) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(body ?? string.Empty, _JsonOptions); }
            catch (JsonException) { return null; }
        }
    }
}
