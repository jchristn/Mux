namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills.Packaging;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Routes for the opt-in skill packs shipped with mux: list them, show one with its skills' installed state, and
    /// install or remove a pack (or one skill from it) in the user's skills directory. The same operations as
    /// <c>mux skill pack</c> and <c>/packs</c>.
    /// </summary>
    public sealed class SkillPackRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly string? _ApiKey;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public SkillPackRoutes(string? apiKey)
        {
            _ApiKey = apiKey;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The catalog to serve instead of the packs embedded in mux, for embedding hosts and tests. Null (the default)
        /// serves the embedded packs.
        /// </summary>
        public static SkillPackCatalog? CatalogOverride { get; set; }

        /// <summary>
        /// The skills directory to install into instead of the configured one, for tests. Null uses settings.
        /// </summary>
        public static string? SkillsDirectoryOverride { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/skills/packs", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                SkillPackInstaller installer = NewInstaller();
                List<SkillPackDto> packs = new List<SkillPackDto>();
                foreach (SkillPack pack in installer.Catalog.Packs)
                {
                    packs.Add(ToDto(pack, installer, includeSkills: false));
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ListResponse<SkillPackDto>(packs)).ConfigureAwait(false);
            }, Documentation.ApiDoc.SkillPacksList);

            app.Get("/v1.0/api/skills/packs/{id}", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string id = Uri.UnescapeDataString(req.Parameters?["id"] ?? string.Empty);
                SkillPackInstaller installer = NewInstaller();
                SkillPack? pack = installer.Catalog.Find(id);
                if (pack == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiError("NotFound", "No skill pack named '" + id + "'.");
                }

                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(ToDto(pack, installer, includeSkills: true)).ConfigureAwait(false);
            }, Documentation.ApiDoc.SkillPackGet);

            app.Post("/v1.0/api/skills/packs/install", async (req) => await ChangeAsync(req.Http, install: true).ConfigureAwait(false), Documentation.ApiDoc.SkillPackInstall);
            app.Post("/v1.0/api/skills/packs/remove", async (req) => await ChangeAsync(req.Http, install: false).ConfigureAwait(false), Documentation.ApiDoc.SkillPackRemove);
        }

        #endregion

        #region Private-Methods

        private async Task<object> ChangeAsync(HttpContextBase ctx, bool install)
        {
            if (!ApiAuth.Authorize(ctx, _ApiKey)) return Unauthorized(ctx);
            SkillPackRequestDto? body;
            try
            {
                string raw = ctx.Request.DataAsString ?? string.Empty;
                body = raw.Trim().Length == 0 ? null : JsonSerializer.Deserialize<SkillPackRequestDto>(raw, _JsonOptions);
            }
            catch (JsonException)
            {
                ctx.Response.StatusCode = 400;
                return new ApiError("BadRequest", "Request body is not valid JSON.");
            }

            if (body == null || string.IsNullOrWhiteSpace(body.Pack))
            {
                ctx.Response.StatusCode = 400;
                return new ApiError("BadRequest", "A 'Pack' is required.");
            }

            try
            {
                SkillPackInstaller installer = NewInstaller();
                SkillPackResult result = install ? installer.Install(body.Pack!, body.Skill, body.Force) : installer.Remove(body.Pack!, body.Skill, body.Force);
                ctx.Response.StatusCode = 200;
                return await Task.FromResult<object>(new SkillPackResultDto { Pack = result.Pack, Installed = result.Installed, Removed = result.Removed, Skipped = result.Skipped }).ConfigureAwait(false);
            }
            catch (KeyNotFoundException ex)
            {
                ctx.Response.StatusCode = 404;
                return new ApiError("NotFound", ex.Message);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
            {
                ctx.Response.StatusCode = 500;
                return new ApiError("PackFailed", ex.Message);
            }
        }

        private static SkillPackInstaller NewInstaller()
        {
            string directory = SkillsDirectoryOverride ?? SettingsLoader.ResolveSkillsDirectory(LoadSettingsSafe());
            return new SkillPackInstaller(directory, CatalogOverride);
        }

        private static SkillPackDto ToDto(SkillPack pack, SkillPackInstaller installer, bool includeSkills)
        {
            SkillPackDto dto = new SkillPackDto
            {
                Id = pack.Id,
                Title = pack.Title,
                Description = pack.Description,
                Category = pack.Category,
                Source = pack.Source,
                License = pack.License,
                SkillCount = pack.Skills.Count,
                InstalledCount = installer.InstalledCount(pack.Id)
            };
            if (includeSkills)
            {
                dto.Skills = new List<SkillPackSkillDto>();
                foreach (BundledSkill skill in pack.Skills)
                {
                    string markdown = skill.SkillMarkdown;
                    dto.Skills.Add(new SkillPackSkillDto
                    {
                        Id = skill.Id,
                        Description = SkillImportNormalizer.ReadFrontmatterValue(markdown, "description") ?? string.Empty,
                        Category = SkillImportNormalizer.ReadFrontmatterValue(markdown, "category") ?? string.Empty,
                        Installed = installer.IsInstalled(pack.Id, skill.Id),
                        FileCount = skill.Files.Count
                    });
                }
            }

            return dto;
        }

        private static MuxSettings LoadSettingsSafe()
        {
            try { return SettingsLoader.LoadSettings(); } catch (Exception) { return new MuxSettings(); }
        }

        private static object Unauthorized(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        #endregion
    }
}
