namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Mux.Core.Sessions;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// Read-only routes over persisted sessions.
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
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

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
                        UpdatedUtc = snapshot.UpdatedUtc
                    });
                }

                req.Http.Response.StatusCode = 200;
                return (object)new ListResponse<SessionSummary>(items);
            });
        }
    }
}
