namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Memory;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Routes for persistent memory: list (and search), save, and delete the memories visible from a working
    /// directory, the same store the terminal, desktop, and the model's <c>remember</c> tool use.
    /// </summary>
    public sealed class MemoryRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly string? _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public MemoryRoutes(string? apiKey)
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

            app.Get("/v1.0/api/memory", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? directory = ResolveDirectory(req.Http, req.Http.Request.Query.Elements["workingDirectory"], out object? error);
                if (directory == null) return error!;
                string? query = Decode(req.Http.Request.Query.Elements["query"]);
                string? name = Decode(req.Http.Request.Query.Elements["name"]);
                MemoryStore store = MemoryStore.FromConfigDirectory();
                List<MemoryEntry> entries;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    MemoryEntry? one = store.Get(name, null, directory);
                    if (one == null)
                    {
                        req.Http.Response.StatusCode = 404;
                        return new ApiError("NotFound", "No memory named '" + name + "'.");
                    }

                    entries = new List<MemoryEntry> { one };
                }
                else
                {
                    entries = store.Search(query, directory);
                }

                MemoryListDto result = new MemoryListDto
                {
                    WorkingDirectory = directory,
                    ProjectKey = MemoryStore.ProjectKey(directory),
                    Enabled = LoadSettingsSafe().MemoryEnabled,
                    Memories = entries.ConvertAll(ToDto)
                };
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(result).ConfigureAwait(false);
            }, Documentation.ApiDoc.MemoryGet);

            app.Post("/v1.0/api/memory", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                MemorySaveRequestDto? payload;
                try
                {
                    payload = JsonSerializer.Deserialize<MemorySaveRequestDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (JsonException)
                {
                    payload = null;
                }

                if (payload == null || string.IsNullOrWhiteSpace(payload.Name))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "A 'Name' is required.");
                }

                MemoryScopeEnum scope = MemoryScopeEnum.Project;
                if (!string.IsNullOrWhiteSpace(payload.Scope) && !MemoryStore.TryParseScope(payload.Scope, out scope))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "Scope must be 'project' or 'global'.");
                }

                string? directory = ResolveDirectory(req.Http, payload.WorkingDirectory, out object? error);
                if (directory == null) return error!;
                try
                {
                    MemoryEntry entry = MemoryStore.FromConfigDirectory().Save(payload.Name!, payload.Description, payload.Content, scope, directory, out bool created);
                    req.Http.Response.StatusCode = created ? 201 : 200;
                    return await Task.FromResult<object>(ToDto(entry)).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", ex.Message);
                }
            }, Documentation.ApiDoc.MemoryPost);

            app.Delete("/v1.0/api/memory", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? name = Decode(req.Http.Request.Query.Elements["name"]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "A 'name' query parameter is required.");
                }

                MemoryScopeEnum? scope = null;
                string? scopeText = Decode(req.Http.Request.Query.Elements["scope"]);
                if (!string.IsNullOrWhiteSpace(scopeText))
                {
                    if (!MemoryStore.TryParseScope(scopeText, out MemoryScopeEnum parsed))
                    {
                        req.Http.Response.StatusCode = 400;
                        return new ApiError("BadRequest", "scope must be 'project' or 'global'.");
                    }

                    scope = parsed;
                }

                string? directory = ResolveDirectory(req.Http, req.Http.Request.Query.Elements["workingDirectory"], out object? error);
                if (directory == null) return error!;
                MemoryEntry? deleted = MemoryStore.FromConfigDirectory().Delete(name, scope, directory);
                if (deleted == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiError("NotFound", "No memory named '" + name + "'.");
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(ToDto(deleted)).ConfigureAwait(false);
            }, Documentation.ApiDoc.MemoryDelete);
        }

        #endregion

        #region Private-Methods

        private static MemoryDto ToDto(MemoryEntry entry)
        {
            return new MemoryDto
            {
                Name = entry.Slug,
                Scope = entry.Scope == MemoryScopeEnum.Global ? "global" : "project",
                Description = entry.Description,
                Content = entry.Content,
                CreatedUtc = entry.CreatedUtc,
                UpdatedUtc = entry.UpdatedUtc
            };
        }

        private static string? ResolveDirectory(HttpContextBase ctx, string? requested, out object? error)
        {
            error = null;
            // Query values may arrive still percent-encoded; decode once (a real path rarely contains '%').
            string directory = string.IsNullOrWhiteSpace(requested) ? Directory.GetCurrentDirectory() : Uri.UnescapeDataString(requested!);
            if (!Directory.Exists(directory))
            {
                ctx.Response.StatusCode = 400;
                error = new ApiError("BadRequest", "workingDirectory does not exist: " + directory);
                return null;
            }

            return directory;
        }

        private static string? Decode(string? value)
        {
            return string.IsNullOrEmpty(value) ? value : Uri.UnescapeDataString(value);
        }

        private static object Unauthorized(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        private static MuxSettings LoadSettingsSafe()
        {
            try { return SettingsLoader.LoadSettings(); } catch (Exception) { return new MuxSettings(); }
        }

        #endregion
    }
}
