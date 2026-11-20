namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Models;
    using Mux.Core.Runs;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// Manual compaction of a saved conversation (the desktop and terminal <c>/compact</c>): older turns are summarized
    /// by the model so the conversation fits the context window again.
    /// </summary>
    public sealed class SessionCompactRoutes
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private readonly string? _ApiKey;
        private readonly SessionStore _SessionStore;
        private readonly RunRegistry _Runs;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="sessionStore">The shared session store.</param>
        /// <param name="runs">The run registry, to refuse compaction while the session has a run in flight and to tell
        /// viewers the transcript changed.</param>
        /// <param name="endpointsProvider">Returns the configured endpoints.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public SessionCompactRoutes(string? apiKey, SessionStore sessionStore, RunRegistry runs, Func<List<EndpointConfig>> endpointsProvider)
        {
            _ApiKey = apiKey;
            _SessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
            _Runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="app">Watson webserver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Post("/v1.0/api/sessions/{id}/compact", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    req.Http.Response.StatusCode = 401;
                    return new ApiError("Unauthorized", "Authentication required.");
                }

                string id = req.Parameters?["id"] ?? string.Empty;
                SessionCompactRequest body = new SessionCompactRequest();
                string raw = req.Http.Request.DataAsString ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    try
                    {
                        body = JsonSerializer.Deserialize<SessionCompactRequest>(raw, _JsonOptions) ?? new SessionCompactRequest();
                    }
                    catch (JsonException)
                    {
                        req.Http.Response.StatusCode = 400;
                        return new ApiError("BadRequest", "Request body is not valid JSON.");
                    }
                }

                SessionSnapshot? snapshot = await _SessionStore.LoadAsync(id, req.Http.Token).ConfigureAwait(false);
                if (snapshot == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return new ApiError("NotFound", "No session has the id '" + id + "'.");
                }

                if (_Runs.FindBySession(id).Any(r => !r.IsTerminal))
                {
                    req.Http.Response.StatusCode = 409;
                    return new ApiError("Conflict", "A run is in progress for this conversation. Stop it or wait for it to finish, then compact.");
                }

                EndpointConfig? endpoint = ResolveEndpoint(body.Endpoint, snapshot.EndpointName);
                if (endpoint == null)
                {
                    req.Http.Response.StatusCode = 400;
                    return new ApiError("BadRequest", "No endpoint is available to write the summary.");
                }

                MuxSettings settings = SettingsLoader.LoadSettings();
                ContextWindowManager estimator = new ContextWindowManager(endpoint.ContextWindow, settings.TokenEstimationRatio, settings.ContextWindowSafetyMarginPercent);
                List<ConversationMessage> before = new List<ConversationMessage>(snapshot.ConversationHistory);
                SessionCompactResult result = new SessionCompactResult
                {
                    Endpoint = endpoint.Name,
                    MessagesBefore = before.Count,
                    MessagesAfter = before.Count,
                    EstimatedTokensBefore = estimator.EstimateTokens(before),
                    ContextWindow = endpoint.ContextWindow
                };
                result.EstimatedTokensAfter = result.EstimatedTokensBefore;

                if (before.Count == 0)
                {
                    result.Message = "Nothing to compact.";
                    req.Http.Response.StatusCode = 200;
                    return result;
                }

                CompactionResult compaction = await ConversationCompactor.CompactAsync(
                    before,
                    endpoint,
                    SettingsLoader.GetActivePromptProfile().CompactionPrompt ?? string.Empty,
                    settings.CompactionPreserveTurns,
                    settings.IgnoreCertErrors,
                    req.Http.Token).ConfigureAwait(false);

                result.Message = compaction.Message;
                if (!compaction.Compacted || compaction.History == null)
                {
                    req.Http.Response.StatusCode = compaction.Success ? 200 : 502;
                    return compaction.Success ? result : new ApiError("CompactionFailed", compaction.Message);
                }

                // Saved straight to the store: the turn-save path refuses to shorten a conversation, which is exactly
                // what compaction does.
                snapshot.ConversationHistory = compaction.History;
                snapshot.CompactionCount++;
                snapshot.UpdatedUtc = DateTime.UtcNow;
                await _SessionStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
                _Runs.NotifyTranscriptChanged(id);

                result.Compacted = true;
                result.MessagesAfter = compaction.History.Count;
                result.EstimatedTokensAfter = estimator.EstimateTokens(compaction.History);
                req.Http.Response.StatusCode = 200;
                return result;
            }, Documentation.ApiDoc.SessionCompact);
        }

        #endregion

        #region Private-Methods

        private EndpointConfig? ResolveEndpoint(string? requested, string? sessionEndpoint)
        {
            List<EndpointConfig> endpoints = _EndpointsProvider();
            foreach (string? name in new[] { requested, sessionEndpoint })
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                EndpointConfig? match = endpoints.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
                if (match != null) return match;
            }

            return endpoints.FirstOrDefault(e => e.IsDefault) ?? endpoints.FirstOrDefault();
        }

        #endregion
    }
}
