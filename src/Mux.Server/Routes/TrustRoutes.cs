namespace Mux.Server.Routes
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Utility;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Routes for project trust: the decision that gates a project's checked-in skills that carry commands. The same
    /// operations as the desktop and terminal <c>/trust</c> command. Changes through the Mux Cloud relay are allowed
    /// (a remote user has the same rights as a local one).
    /// </summary>
    public sealed class TrustRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;
        private readonly Func<SkillRuntime?> _SkillsProvider;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="skillsProvider">Returns the hub's skills runtime when one is running, so its cached project
        /// views refresh; null falls back to the trust file alone.</param>
        public TrustRoutes(string? apiKey, Func<SkillRuntime?>? skillsProvider)
        {
            _ApiKey = apiKey;
            _SkillsProvider = skillsProvider ?? (() => null);
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/trust", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? requested = req.Http.Request.Query.Elements["workingDirectory"];
                string directory = string.IsNullOrWhiteSpace(requested) ? Directory.GetCurrentDirectory() : Uri.UnescapeDataString(requested!);
                if (!Directory.Exists(directory))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "workingDirectory does not exist: " + directory);
                }

                string root = RepositoryRootLocator.FindProjectRoot(directory) ?? directory;
                ProjectTrustLevelEnum level = new ProjectTrustStore(SettingsLoader.GetTrustedProjectsPath()).GetLevel(root);
                req.Http.Response.StatusCode = 200;
                return await Task.FromResult<object>(new ProjectTrustDto { WorkingDirectory = directory, ProjectRoot = root, Level = ProjectTrustStore.ToWireName(level) }).ConfigureAwait(false);
            }, Documentation.ApiDoc.TrustGet);

            app.Put("/v1.0/api/trust", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                ProjectTrustRequest? body;
                try
                {
                    body = JsonSerializer.Deserialize<ProjectTrustRequest>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (JsonException)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (body == null || string.IsNullOrWhiteSpace(body.WorkingDirectory) || !Directory.Exists(body.WorkingDirectory))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "'workingDirectory' must name an existing directory.");
                }

                if (!ProjectTrustStore.TryParseLevel(body.Level, out ProjectTrustLevelEnum level))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "'level' must be all, playbooks, ignore, or reset.");
                }

                try
                {
                    string root;
                    SkillRuntime? runtime = _SkillsProvider();
                    if (runtime?.TrustStore != null)
                    {
                        root = runtime.SetProjectTrust(body.WorkingDirectory, level);
                    }
                    else
                    {
                        root = RepositoryRootLocator.FindProjectRoot(body.WorkingDirectory) ?? body.WorkingDirectory;
                        new ProjectTrustStore(SettingsLoader.GetTrustedProjectsPath()).SetLevel(root, level);
                    }

                    req.Http.Response.StatusCode = 200;
                    return await Task.FromResult<object>(new ProjectTrustDto { WorkingDirectory = body.WorkingDirectory, ProjectRoot = root, Level = ProjectTrustStore.ToWireName(level) }).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    req.Http.Response.StatusCode = 500;
                    return new ApiError("SaveFailed", "Could not record trust: " + ex.Message);
                }
            }, Documentation.ApiDoc.TrustPut);
        }

        #endregion

        #region Private-Methods

        private static object Unauthorized(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        #endregion
    }
}
