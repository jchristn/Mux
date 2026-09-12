namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Routes over persisted sessions: list, export to Markdown/HTML (via <see cref="SessionExporter"/>), and
    /// delete.
    /// </summary>
    public sealed class SessionRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

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

            // Load a single session's full conversation so the dashboard can open and continue it.
            app.Get("/v1.0/api/sessions/detail", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                string? id = req.Http.Request.Query.Elements["id"];
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' query parameter is required."); }

                SessionSnapshot? snapshot = await _SessionStore.LoadAsync(id, req.Http.Token).ConfigureAwait(false);
                if (snapshot == null) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No session with id " + id + "."); }

                SessionDetailDto detail = new SessionDetailDto
                {
                    Id = snapshot.Id,
                    Title = snapshot.Title,
                    EndpointName = snapshot.EndpointName,
                    Model = snapshot.Model
                };
                foreach (ConversationMessage message in snapshot.ConversationHistory)
                {
                    detail.Messages.Add(new ChatMessageDto { Role = RoleToString(message.Role), Content = message.Content ?? string.Empty });
                }

                req.Http.Response.StatusCode = 200;
                return (object)detail;
            });

            // Create or update (upsert) a conversation from the dashboard chat so it is persisted like a TUI or
            // desktop session — appearing in the session list on every surface and reopenable to continue.
            app.Put("/v1.0/api/sessions", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                SessionSaveRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<SessionSaveRequest>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (request == null || request.Messages == null || request.Messages.Count == 0)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A non-empty 'messages' array is required.");
                }

                SessionSnapshot? existing = null;
                if (!string.IsNullOrWhiteSpace(request.Id))
                {
                    existing = await _SessionStore.LoadAsync(request.Id!, req.Http.Token).ConfigureAwait(false);
                }

                DateTime now = DateTime.UtcNow;
                SessionSnapshot snapshot = existing ?? new SessionSnapshot
                {
                    Id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id!,
                    CreatedUtc = now
                };

                string? firstUser = request.Messages.FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content;
                snapshot.Title = SessionTitleHelper.Normalize(
                    string.IsNullOrWhiteSpace(request.Title) ? firstUser : request.Title,
                    SessionTitleHelper.DefaultTitle);
                snapshot.EndpointName = request.EndpointName ?? string.Empty;
                snapshot.Model = request.Model ?? string.Empty;
                snapshot.UpdatedUtc = now;
                snapshot.ConversationHistory = request.Messages
                    .Select(m => new ConversationMessage { Role = ParseRole(m.Role), Content = m.Content ?? string.Empty })
                    .ToList();

                try
                {
                    await _SessionStore.SaveAsync(snapshot, req.Http.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", ex.Message);
                }

                req.Http.Response.StatusCode = 200;
                return (object)new SessionSummary
                {
                    Id = snapshot.Id,
                    Title = snapshot.Title,
                    EndpointName = snapshot.EndpointName,
                    Model = snapshot.Model,
                    CreatedUtc = snapshot.CreatedUtc,
                    UpdatedUtc = snapshot.UpdatedUtc,
                    MessageCount = snapshot.ConversationHistory.Count
                };
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

        private static RoleEnum ParseRole(string? role)
        {
            switch ((role ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "system": return RoleEnum.System;
                case "assistant": return RoleEnum.Assistant;
                case "tool": return RoleEnum.Tool;
                default: return RoleEnum.User;
            }
        }

        private static string RoleToString(RoleEnum role)
        {
            switch (role)
            {
                case RoleEnum.System: return "system";
                case RoleEnum.Assistant: return "assistant";
                case RoleEnum.Tool: return "tool";
                default: return "user";
            }
        }
    }
}
