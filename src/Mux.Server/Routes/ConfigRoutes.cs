namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Plugins;
    using Mux.Core.Settings;
    using Mux.Core.Subagents;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// GET/PUT collection routes for the secret-free config domains: prompt profiles, subagents, hooks and
    /// custom commands, and keybindings. Each edits its own <c>~/.mux</c> file through <see cref="SettingsLoader"/>.
    /// </summary>
    public sealed class ConfigRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public ConfigRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            RegisterPrompts(app);
            RegisterSubagents(app);
            RegisterHooks(app);
            RegisterKeybindings(app);
        }

        private object Unauthorized() => new ApiError("Unauthorized", "Authentication required.");

        private T? Parse<T>(string body) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(body ?? string.Empty, _JsonOptions); }
            catch (Exception) { return null; }
        }

        #region Prompts

        private void RegisterPrompts(Webserver app)
        {
            app.Get("/v1.0/api/prompts", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                List<PromptProfileDto> items = SettingsLoader.LoadPrompts().Select(p => new PromptProfileDto
                {
                    Name = p.Name,
                    IsActive = p.IsActive,
                    SystemPrompt = p.SystemPrompt
                }).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<PromptProfileDto>(items)).ConfigureAwait(false);
            });

            app.Put("/v1.0/api/prompts", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                ListResponse<PromptProfileDto>? payload = Parse<ListResponse<PromptProfileDto>>(req.Http.Request.DataAsString);
                if (payload?.Items == null) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'items' array is required."); }

                try
                {
                    Dictionary<string, PromptProfile> existing = SettingsLoader.LoadPrompts()
                        .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                    List<PromptProfile> merged = new List<PromptProfile>();
                    foreach (PromptProfileDto dto in payload.Items)
                    {
                        if (string.IsNullOrWhiteSpace(dto.Name)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Every prompt profile needs a name."); }
                        existing.TryGetValue(dto.Name, out PromptProfile? prior);
                        merged.Add(new PromptProfile
                        {
                            Name = dto.Name.Trim(),
                            IsActive = dto.IsActive,
                            SystemPrompt = dto.SystemPrompt ?? string.Empty,
                            // Preserve the advanced prompt fields not edited from the dashboard.
                            ToolsDisabledPrompt = prior?.ToolsDisabledPrompt ?? string.Empty,
                            CompactionPrompt = prior?.CompactionPrompt ?? string.Empty
                        });
                    }

                    SettingsLoader.SavePrompts(merged);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<PromptProfileDto>(SettingsLoader.LoadPrompts().Select(p => new PromptProfileDto { Name = p.Name, IsActive = p.IsActive, SystemPrompt = p.SystemPrompt }).ToList())).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", ex.Message); }
            });
        }

        #endregion

        #region Subagents

        private void RegisterSubagents(Webserver app)
        {
            app.Get("/v1.0/api/subagents", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                List<SubagentDto> items = SettingsLoader.LoadSubagents().Select(s => new SubagentDto
                {
                    Name = s.Name,
                    Description = s.Description,
                    SystemPrompt = s.SystemPrompt,
                    EndpointName = s.EndpointName,
                    AllowedTools = new List<string>(s.AllowedTools),
                    MaxIterations = s.MaxIterations
                }).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<SubagentDto>(items)).ConfigureAwait(false);
            });

            app.Put("/v1.0/api/subagents", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                ListResponse<SubagentDto>? payload = Parse<ListResponse<SubagentDto>>(req.Http.Request.DataAsString);
                if (payload?.Items == null) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'items' array is required."); }

                try
                {
                    List<SubagentDefinition> merged = payload.Items.Select(dto => new SubagentDefinition
                    {
                        Name = (dto.Name ?? string.Empty).Trim(),
                        Description = dto.Description ?? string.Empty,
                        SystemPrompt = dto.SystemPrompt ?? string.Empty,
                        EndpointName = string.IsNullOrWhiteSpace(dto.EndpointName) ? null : dto.EndpointName,
                        AllowedTools = dto.AllowedTools ?? new List<string>(),
                        MaxIterations = dto.MaxIterations
                    }).ToList();

                    SettingsLoader.SaveSubagents(merged);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<SubagentDto>(SettingsLoader.LoadSubagents().Select(s => new SubagentDto { Name = s.Name, Description = s.Description, SystemPrompt = s.SystemPrompt, EndpointName = s.EndpointName, AllowedTools = new List<string>(s.AllowedTools), MaxIterations = s.MaxIterations }).ToList())).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", ex.Message); }
            });
        }

        #endregion

        #region Hooks

        private void RegisterHooks(Webserver app)
        {
            app.Get("/v1.0/api/hooks", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                PluginConfig config = SettingsLoader.LoadPluginConfig();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(ToDto(config)).ConfigureAwait(false);
            });

            app.Put("/v1.0/api/hooks", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                PluginConfigDto? dto = Parse<PluginConfigDto>(req.Http.Request.DataAsString);
                if (dto == null) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "A plugin config object is required."); }

                try
                {
                    PluginConfig config = new PluginConfig
                    {
                        Hooks = (dto.Hooks ?? new List<HookDto>()).Select(h =>
                        {
                            HookEventEnumConverter.TryParse(h.Event, out HookEventEnum ev);
                            return new HookDefinition
                            {
                                Name = h.Name ?? string.Empty,
                                Event = ev,
                                Command = h.Command ?? string.Empty,
                                Args = h.Args ?? new List<string>(),
                                Blocking = h.Blocking,
                                TimeoutMs = h.TimeoutMs
                            };
                        }).ToList(),
                        Commands = (dto.Commands ?? new List<CustomCommandDto>()).Select(c => new CustomCommandDefinition
                        {
                            Name = c.Name ?? string.Empty,
                            Description = c.Description ?? string.Empty,
                            Command = c.Command ?? string.Empty,
                            Args = c.Args ?? new List<string>(),
                            TimeoutMs = c.TimeoutMs
                        }).ToList()
                    };

                    SettingsLoader.SavePluginConfig(config);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(ToDto(SettingsLoader.LoadPluginConfig())).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", ex.Message); }
            });
        }

        private static PluginConfigDto ToDto(PluginConfig config)
        {
            return new PluginConfigDto
            {
                Hooks = config.Hooks.Select(h => new HookDto
                {
                    Name = h.Name,
                    Event = HookEventEnumConverter.ToWireName(h.Event),
                    Command = h.Command,
                    Args = new List<string>(h.Args),
                    Blocking = h.Blocking,
                    TimeoutMs = h.TimeoutMs
                }).ToList(),
                Commands = config.Commands.Select(c => new CustomCommandDto
                {
                    Name = c.Name,
                    Description = c.Description,
                    Command = c.Command,
                    Args = new List<string>(c.Args),
                    TimeoutMs = c.TimeoutMs
                }).ToList()
            };
        }

        #endregion

        #region Keybindings

        private void RegisterKeybindings(Webserver app)
        {
            app.Get("/v1.0/api/keybindings", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                List<KeybindingDto> items = SettingsLoader.LoadKeybindings().Select(kv => new KeybindingDto { CommandId = kv.Key, Chord = kv.Value }).ToList();
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<KeybindingDto>(items)).ConfigureAwait(false);
            });

            app.Put("/v1.0/api/keybindings", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                ListResponse<KeybindingDto>? payload = Parse<ListResponse<KeybindingDto>>(req.Http.Request.DataAsString);
                if (payload?.Items == null) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'items' array is required."); }

                try
                {
                    Dictionary<string, string?> map = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (KeybindingDto dto in payload.Items)
                    {
                        if (string.IsNullOrWhiteSpace(dto.CommandId)) continue;
                        map[dto.CommandId.Trim()] = string.IsNullOrWhiteSpace(dto.Chord) ? null : dto.Chord.Trim();
                    }

                    SettingsLoader.SaveKeybindings(map);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<KeybindingDto>(SettingsLoader.LoadKeybindings().Select(kv => new KeybindingDto { CommandId = kv.Key, Chord = kv.Value }).ToList())).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", ex.Message); }
            });
        }

        #endregion
    }
}
