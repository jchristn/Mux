namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Routes over persisted sessions: list, export to Markdown/HTML (via <see cref="SessionExporter"/>), and
    /// delete.
    /// </summary>
    public sealed class SessionRoutes
    {
        private readonly string? _ApiKey;
        private readonly SessionStore _SessionStore;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="sessionStore">Session store.</param>
        public SessionRoutes(string? apiKey, SessionStore sessionStore)
        {
            _ApiKey = apiKey;
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Get("/v1.0/api/sessions", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                IReadOnlyList<SessionSnapshot> snapshots = await _SessionStore.ListAsync(req.Http.Token).ConfigureAwait(false);
                List<SessionSummary> items = new List<SessionSummary>();
                foreach (SessionSnapshot snapshot in snapshots)
                {
                    items.Add(new SessionSummary
                    {
                        Id = snapshot.Id,
                        Title = snapshot.Title,
                        EndpointName = snapshot.EndpointName,
                        Model = snapshot.Model,
                        CreatedUtc = snapshot.CreatedUtc,
                        UpdatedUtc = snapshot.UpdatedUtc,
                        MessageCount = snapshot.ConversationHistory.Count
                    });
                }

                req.Http.Response.StatusCode = 200;
                return (object)new ListResponse<SessionSummary>(items);
            });

            // Render a session to Markdown or HTML and return it for client-side download.
            app.Get("/v1.0/api/sessions/export", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                string? id = req.Http.Request.Query.Elements["id"];
                string format = req.Http.Request.Query.Elements["format"] ?? "md";
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' query parameter is required."); }
                if (!SessionExporter.TryNormalizeFormat(format, out string ext)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "Format must be 'md' or 'html'."); }

                SessionSnapshot? snapshot = await _SessionStore.LoadAsync(id, req.Http.Token).ConfigureAwait(false);
                if (snapshot == null) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No session with id " + id + "."); }

                string content = ext == "html" ? SessionExporter.ToHtml(snapshot) : SessionExporter.ToMarkdown(snapshot);
                req.Http.Response.StatusCode = 200;
                return (object)new SessionExportDto { Format = ext, Filename = id + "." + ext, Content = content };
            });

            app.Delete("/v1.0/api/sessions", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                string? id = req.Http.Request.Query.Elements["id"];
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' query parameter is required."); }

                try
                {
                    await _SessionStore.DeleteAsync(id, req.Http.Token).ConfigureAwait(false);
                    IReadOnlyList<SessionSnapshot> snapshots = await _SessionStore.ListAsync(req.Http.Token).ConfigureAwait(false);
                    List<SessionSummary> items = new List<SessionSummary>();
                    foreach (SessionSnapshot snapshot in snapshots)
                    {
                        items.Add(new SessionSummary { Id = snapshot.Id, Title = snapshot.Title, EndpointName = snapshot.EndpointName, Model = snapshot.Model, CreatedUtc = snapshot.CreatedUtc, UpdatedUtc = snapshot.UpdatedUtc });
                    }

                    req.Http.Response.StatusCode = 200;
                    return (object)new ListResponse<SessionSummary>(items);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("DeleteFailed", ex.Message); }
            });
        }
    }
}
