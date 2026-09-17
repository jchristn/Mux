namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Runs;
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
        private readonly SessionManager _SessionManager;
        private readonly RunRegistry? _Runs;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="sessionStore">Session store.</param>
        /// <param name="runs">Optional run registry; when set, upserts and deletes broadcast a conversation-list
        /// change so every surface refreshes its list without a manual refresh.</param>
        public SessionRoutes(string? apiKey, SessionStore sessionStore, RunRegistry? runs = null)
        {
            _ApiKey = apiKey;
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _SessionManager = new SessionManager(_SessionStore);
            _Runs = runs;
        }

        private static List<SessionTagDto> ToTagDtos(IEnumerable<SessionTag> tags)
        {
            List<SessionTagDto> dtos = new List<SessionTagDto>();
            foreach (SessionTag tag in tags)
            {
                dtos.Add(new SessionTagDto { Key = tag.Key, Value = tag.Value });
            }

            return dtos;
        }

        // Additively apply labels/tags to a snapshot in place, normalizing through the shared rules and
        // skipping any that fail. Labels dedupe case-insensitively; tags upsert by normalized key.
        private static void ApplyMetadata(SessionSnapshot snapshot, IEnumerable<string>? labels, IEnumerable<SessionTagDto>? tags)
        {
            if (labels != null)
            {
                foreach (string raw in labels)
                {
                    if (!SessionMetadataNormalizer.TryNormalizeLabel(raw, out string normalized, out _)) continue;
                    if (!snapshot.Labels.Exists(l => string.Equals(l, normalized, StringComparison.OrdinalIgnoreCase)))
                    {
                        snapshot.Labels.Add(normalized);
                    }
                }
            }

            if (tags != null)
            {
                foreach (SessionTagDto raw in tags)
                {
                    if (raw == null) continue;
                    if (!SessionMetadataNormalizer.TryNormalizeTagKey(raw.Key, out string key, out _)) continue;
                    if (!SessionMetadataNormalizer.TryNormalizeTagValue(raw.Value, out string value, out _)) continue;

                    SessionTag? existing = snapshot.Tags.Find(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (existing != null)
                    {
                        existing.Value = value;
                    }
                    else
                    {
                        snapshot.Tags.Add(new SessionTag(key, value));
                    }
                }
            }
        }

        private static SessionSummary ToSummary(SessionSnapshot snapshot)
        {
            return new SessionSummary
            {
                Id = snapshot.Id,
                Title = snapshot.Title,
                EndpointName = snapshot.EndpointName,
                Model = snapshot.Model,
                CreatedUtc = snapshot.CreatedUtc,
                UpdatedUtc = snapshot.UpdatedUtc,
                MessageCount = snapshot.ConversationHistory.Count,
                Labels = new List<string>(snapshot.Labels),
                Tags = ToTagDtos(snapshot.Tags)
            };
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
                    items.Add(ToSummary(snapshot));
                }

                req.Http.Response.StatusCode = 200;
                return (object)new ListResponse<SessionSummary>(items);
            }, Documentation.ApiDoc.SessionsList);

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
                    Model = snapshot.Model,
                    Labels = new List<string>(snapshot.Labels),
                    Tags = ToTagDtos(snapshot.Tags)
                };
                detail.Messages.AddRange(ChatMessageMapper.ToDtoList(snapshot.ConversationHistory));

                req.Http.Response.StatusCode = 200;
                return (object)detail;
            }, Documentation.ApiDoc.SessionsDetail);

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
                snapshot.ConversationHistory = ChatMessageMapper.ToModelList(request.Messages);

                // Apply any supplied metadata additively, normalizing through the shared rules. Existing labels
                // and tags on the loaded snapshot are preserved; a new label is deduped, a tag upserts by key.
                ApplyMetadata(snapshot, request.Labels, request.Tags);

                try
                {
                    await _SessionStore.SaveAsync(snapshot, req.Http.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 500;
                    return (object)new ApiError("SaveFailed", ex.Message);
                }

                _Runs?.NotifyTranscriptChanged(snapshot.Id);
                _Runs?.NotifySessionsChanged(snapshot.Id);
                req.Http.Response.StatusCode = 200;
                return (object)ToSummary(snapshot);
            }, Documentation.ApiDoc.SessionsPut);

            // Incrementally edit a session's labels/tags without round-tripping the whole conversation.
            app.Post("/v1.0/api/sessions/{id}/metadata", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                string? id = req.Parameters?["id"];
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "A session id is required in the path."); }

                SessionMetadataPatch? patch;
                try
                {
                    patch = JsonSerializer.Deserialize<SessionMetadataPatch>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (patch == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "A metadata patch body is required.");
                }

                SessionSnapshot? snapshot = await _SessionStore.LoadAsync(id!, req.Http.Token).ConfigureAwait(false);
                if (snapshot == null) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No session with id " + id + "."); }

                try
                {
                    if (patch.SetTags != null)
                    {
                        foreach (SessionTagDto tag in patch.SetTags)
                        {
                            _ = await _SessionManager.SetTagAsync(id!, tag.Key, tag.Value, req.Http.Token).ConfigureAwait(false);
                        }
                    }

                    if (patch.AddLabels != null)
                    {
                        foreach (string label in patch.AddLabels)
                        {
                            _ = await _SessionManager.AddLabelAsync(id!, label, req.Http.Token).ConfigureAwait(false);
                        }
                    }

                    if (patch.RemoveLabels != null)
                    {
                        foreach (string label in patch.RemoveLabels)
                        {
                            _ = await _SessionManager.RemoveLabelAsync(id!, label, req.Http.Token).ConfigureAwait(false);
                        }
                    }

                    if (patch.RemoveTagKeys != null)
                    {
                        foreach (string key in patch.RemoveTagKeys)
                        {
                            _ = await _SessionManager.RemoveTagAsync(id!, key, req.Http.Token).ConfigureAwait(false);
                        }
                    }
                }
                catch (ArgumentException ex)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", ex.Message);
                }

                SessionSnapshot? updated = await _SessionStore.LoadAsync(id!, req.Http.Token).ConfigureAwait(false);
                if (updated == null) { req.Http.Response.StatusCode = 404; return (object)new ApiError("NotFound", "No session with id " + id + "."); }

                _Runs?.NotifySessionsChanged(updated.Id);
                req.Http.Response.StatusCode = 200;
                return (object)ToSummary(updated);
            }, Documentation.ApiDoc.SessionsMetadata);

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
            }, Documentation.ApiDoc.SessionsExport);

            app.Delete("/v1.0/api/sessions", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey)) return (object)new ApiError("Unauthorized", "Authentication required.");

                string? id = req.Http.Request.Query.Elements["id"];
                if (string.IsNullOrWhiteSpace(id)) { req.Http.Response.StatusCode = 400; return (object)new ApiError("BadRequest", "An 'id' query parameter is required."); }

                try
                {
                    await _SessionStore.DeleteAsync(id, req.Http.Token).ConfigureAwait(false);
                    _Runs?.NotifySessionsChanged(id);
                    IReadOnlyList<SessionSnapshot> snapshots = await _SessionStore.ListAsync(req.Http.Token).ConfigureAwait(false);
                    List<SessionSummary> items = new List<SessionSummary>();
                    foreach (SessionSnapshot snapshot in snapshots)
                    {
                        items.Add(ToSummary(snapshot));
                    }

                    req.Http.Response.StatusCode = 200;
                    return (object)new ListResponse<SessionSummary>(items);
                }
                catch (Exception ex) { req.Http.Response.StatusCode = 500; return (object)new ApiError("DeleteFailed", ex.Message); }
            }, Documentation.ApiDoc.SessionsDelete);
        }

    }
}
