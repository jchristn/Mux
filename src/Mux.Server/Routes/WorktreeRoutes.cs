namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Mux.Core.Worktrees;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Routes for the isolated git worktrees mux keeps for subagents and jobs: list them, prune the unchanged ones,
    /// and remove one. The same operations as <c>mux worktree</c> and <c>/worktrees</c>, so the web dashboard, VS Code,
    /// and scripts can manage leftovers without a terminal.
    /// </summary>
    public sealed class WorktreeRoutes
    {
        #region Private-Members

        private readonly string? _ApiKey;
        private readonly WorktreeManager _Manager = new WorktreeManager();

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        public WorktreeRoutes(string? apiKey)
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

            app.Get("/v1.0/api/worktrees", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? directory = ResolveDirectory(req.Http, out object? error);
                if (directory == null) return error!;
                try
                {
                    IReadOnlyList<WorktreeInfo> worktrees = await _Manager.ListAsync(directory, req.Http.Token).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 200;
                    return new ListResponse<WorktreeInfo>(new List<WorktreeInfo>(worktrees));
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", ex.Message);
                }
            }, Documentation.ApiDoc.WorktreesGet);

            app.Post("/v1.0/api/worktrees/prune", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? directory = ResolveDirectory(req.Http, out object? error);
                if (directory == null) return error!;
                try
                {
                    IReadOnlyList<string> removed = await _Manager.PruneAsync(directory, req.Http.Token).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 200;
                    return new ListResponse<string>(new List<string>(removed));
                }
                catch (InvalidOperationException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", ex.Message);
                }
            }, Documentation.ApiDoc.WorktreesPrune);

            app.Delete("/v1.0/api/worktrees", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return Unauthorized(req.Http);
                string? directory = ResolveDirectory(req.Http, out object? error);
                if (directory == null) return error!;
                string? name = Decode(req.Http.Request.Query.Elements["name"]);
                if (string.IsNullOrWhiteSpace(name))
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "A 'name' query parameter is required (a worktree name or its mux/ branch).");
                }

                bool force = IsTrue(req.Http.Request.Query.Elements["force"]);
                bool keepBranch = IsTrue(req.Http.Request.Query.Elements["keepBranch"]);
                try
                {
                    WorktreeInfo removed = await _Manager.RemoveAsync(directory, name!, force, keepBranch, req.Http.Token).ConfigureAwait(false);
                    req.Http.Response.StatusCode = 200;
                    return removed;
                }
                catch (InvalidOperationException ex)
                {
                    // Not found, uncommitted changes, or unmerged commits without --force: the message says which.
                    req.Http.Response.StatusCode = ex.Message.IndexOf("no mux worktree", StringComparison.OrdinalIgnoreCase) >= 0 || ex.Message.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0 ? 404 : 409;
                    return new ApiError(req.Http.Response.StatusCode == 404 ? "NotFound" : "Conflict", ex.Message);
                }
            }, Documentation.ApiDoc.WorktreesDelete);
        }

        #endregion

        #region Private-Methods

        private static string? ResolveDirectory(HttpContextBase ctx, out object? error)
        {
            error = null;
            string? requested = Decode(ctx.Request.Query.Elements["workingDirectory"]);
            string directory = string.IsNullOrWhiteSpace(requested) ? Directory.GetCurrentDirectory() : requested!;
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

        private static bool IsTrue(string? value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
        }

        private static object Unauthorized(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 401;
            return new ApiError("Unauthorized", "Authentication required.");
        }

        #endregion
    }
}
