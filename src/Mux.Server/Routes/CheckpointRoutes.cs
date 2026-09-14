namespace Mux.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Checkpoints;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Per-turn undo/redo over the git checkpoints a run records. Backed by the server's
    /// <see cref="CheckpointRegistry"/>, keyed by working directory, so an editor can undo a turn's file
    /// changes even though each request is a separate HTTP call. Undo and redo restore the working tree only;
    /// they never touch the user's branch, history, or stash.
    /// </summary>
    public sealed class CheckpointRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        private readonly string? _ApiKey;
        private readonly CheckpointRegistry _Registry;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="registry">The server's checkpoint registry.</param>
        public CheckpointRoutes(string? apiKey, CheckpointRegistry registry)
        {
            _ApiKey = apiKey;
            _Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/checkpoints", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                string? dir = req.Http.Request.Query.Elements["workingDirectory"];
                if (string.IsNullOrWhiteSpace(dir)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "A 'workingDirectory' query parameter is required."); }

                CheckpointManager? manager = await _Registry.GetOrCreateAsync(dir, req.Http.Token).ConfigureAwait(false);
                req.Http.Response.StatusCode = 200;
                return (object)new CheckpointStatus
                {
                    WorkingDirectory = dir,
                    IsRepository = manager != null,
                    CanUndo = manager?.CanUndo ?? false,
                    CanRedo = manager?.CanRedo ?? false,
                };
            });

            app.Post("/v1.0/api/checkpoints/undo", async (req) => await ActAsync(req.Http, undo: true).ConfigureAwait(false));
            app.Post("/v1.0/api/checkpoints/redo", async (req) => await ActAsync(req.Http, undo: false).ConfigureAwait(false));
        }

        private async Task<object> ActAsync(WatsonWebserver.Core.HttpContextBase ctx, bool undo)
        {
            if (!ApiAuth.Authorize(ctx, _ApiKey)) return new ApiError("Unauthorized", "Authentication required.");

            CheckpointActionRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<CheckpointActionRequest>(ctx.Request.DataAsString ?? string.Empty, _JsonOptions);
            }
            catch (Exception)
            {
                ctx.Response.StatusCode = 400;
                return new ApiError("BadRequest", "Request body is not valid JSON.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                ctx.Response.StatusCode = 400;
                return new ApiError("BadRequest", "'workingDirectory' is required.");
            }

            CheckpointManager? manager = await _Registry.GetOrCreateAsync(request.WorkingDirectory, ctx.Token).ConfigureAwait(false);
            if (manager == null)
            {
                ctx.Response.StatusCode = 200;
                return new CheckpointActionResult { Restored = false, Label = null, CanUndo = false, CanRedo = false };
            }

            Checkpoint? restored;
            try
            {
                restored = undo
                    ? await manager.UndoAsync(ctx.Token).ConfigureAwait(false)
                    : await manager.RedoAsync(ctx.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ctx.Response.StatusCode = 500;
                return new ApiError(undo ? "UndoFailed" : "RedoFailed", ex.Message);
            }

            ctx.Response.StatusCode = 200;
            return new CheckpointActionResult
            {
                Restored = restored != null,
                Label = restored?.Label,
                CanUndo = manager.CanUndo,
                CanRedo = manager.CanRedo,
            };
        }
    }
}
