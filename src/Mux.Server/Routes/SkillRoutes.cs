namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Routes over the user's skills: a list with per-skill enablement, the SKILL.md body for one skill,
    /// enable/disable, and delete. Backed by <see cref="SkillLoader"/> and <see cref="SkillManager"/>.
    /// </summary>
    public sealed class SkillRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public SkillRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/skills", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                List<SkillDto> items = LoadSkills(includeBody: false);
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<SkillDto>(items)).ConfigureAwait(false);
            });

            app.Get("/v1.0/api/skills/detail", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                string? id = req.Http.Request.Query.Elements["id"];
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' query parameter is required."); }

                SkillDto? found = LoadSkills(includeBody: true).FirstOrDefault(s => string.Equals(s.Name, id, StringComparison.OrdinalIgnoreCase));
                if (found == null) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No skill named " + id + "."); }
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(found).ConfigureAwait(false);
            });

            // Toggle enablement: body { "id": "...", "enabled": true }.
            app.Put("/v1.0/api/skills/enabled", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                ToggleDto? dto;
                try { dto = JsonSerializer.Deserialize<ToggleDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions); }
                catch (Exception) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Request body is not valid JSON."); }
                if (dto == null || string.IsNullOrWhiteSpace(dto.Id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' is required."); }

                try
                {
                    new SkillManager(SkillsDir()).SetEnabled(dto.Id, dto.Enabled);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<SkillDto>(LoadSkills(includeBody: false))).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", ex.Message); }
            });

            app.Delete("/v1.0/api/skills", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                string? id = req.Http.Request.Query.Elements["id"];
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' query parameter is required."); }

                try
                {
                    new SkillManager(SkillsDir()).Remove(id);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<SkillDto>(LoadSkills(includeBody: false))).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("DeleteFailed", ex.Message); }
            });

            // Create a new skill: body { "name": "...", "body": "<SKILL.md text>" }.
            app.Post("/v1.0/api/skills", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                SkillEditDto? dto;
                try { dto = JsonSerializer.Deserialize<SkillEditDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions); }
                catch (Exception) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Request body is not valid JSON."); }
                if (dto == null || string.IsNullOrWhiteSpace(dto.Name)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "A 'name' is required."); }
                if (!Mux.Core.Skills.SkillManager.IsValidId(dto.Name)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Skill name must be a valid folder id (letters, digits, dashes)."); }

                try
                {
                    string skillsDir = SkillsDir();
                    if (Directory.Exists(Path.Combine(skillsDir, dto.Name))) { req.Http.Response.StatusCode = 409; return (object)new ApiError("Conflict", "A skill named '" + dto.Name + "' already exists."); }
                    string dir = new SkillManager(skillsDir).Create(new Mux.Core.Models.SkillScaffold { Id = dto.Name, Title = dto.Name, Description = string.Empty, Mutating = true, Interpreter = "pwsh" });
                    if (!string.IsNullOrWhiteSpace(dto.Body))
                    {
                        File.WriteAllText(Path.Combine(dir, "SKILL.md"), dto.Body);
                    }

                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<SkillDto>(LoadSkills(includeBody: false))).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("CreateFailed", ex.Message); }
            });

            // Overwrite a skill's SKILL.md: body { "id": "...", "body": "..." }.
            app.Put("/v1.0/api/skills/body", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized();
                SkillEditDto? dto;
                try { dto = JsonSerializer.Deserialize<SkillEditDto>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions); }
                catch (Exception) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Request body is not valid JSON."); }
                string id = dto?.Id ?? dto?.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' is required."); }

                try
                {
                    string dir = Path.Combine(SkillsDir(), id);
                    if (!Directory.Exists(dir)) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No skill named " + id + "."); }
                    File.WriteAllText(Path.Combine(dir, "SKILL.md"), dto?.Body ?? string.Empty);
                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ListResponse<SkillDto>(LoadSkills(includeBody: false))).ConfigureAwait(false);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("SaveFailed", ex.Message); }
            });
        }

        private object Unauthorized() => new ApiError("Unauthorized", "Authentication required.");

        private sealed class SkillEditDto
        {
            public string Name { get; set; } = string.Empty;

            public string Id { get; set; } = string.Empty;

            public string Body { get; set; } = string.Empty;
        }

        private static string SkillsDir()
        {
            MuxSettings settings;
            try { settings = SettingsLoader.LoadSettings(); } catch (Exception) { settings = new MuxSettings(); }
            return SettingsLoader.ResolveSkillsDirectory(settings);
        }

        private static List<SkillDto> LoadSkills(bool includeBody)
        {
            string dir = SkillsDir();
            if (!Directory.Exists(dir)) return new List<SkillDto>();

            Dictionary<string, bool> enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (SkillIndexEntry entry in SettingsLoader.LoadSkillIndex())
            {
                if (!string.IsNullOrWhiteSpace(entry.Id)) enabled[entry.Id] = entry.Enabled;
            }

            List<SkillDto> result = new List<SkillDto>();
            foreach (Skill skill in new SkillLoader(dir).Discover())
            {
                bool isEnabled = enabled.TryGetValue(skill.Manifest.Name, out bool e) ? e : skill.Manifest.Enabled;
                result.Add(new SkillDto
                {
                    Name = skill.Manifest.Name,
                    Title = skill.Manifest.Title,
                    Description = skill.Manifest.Description,
                    Enabled = isEnabled,
                    Valid = skill.IsValid,
                    Mutating = skill.Manifest.Mutating,
                    Commands = skill.Manifest.Commands.Count,
                    Errors = new List<string>(skill.Validation.Errors),
                    Body = includeBody ? skill.Body : null
                });
            }

            return result;
        }

        private sealed class ToggleDto
        {
            public string Id { get; set; } = string.Empty;

            public bool Enabled { get; set; }
        }
    }
}
