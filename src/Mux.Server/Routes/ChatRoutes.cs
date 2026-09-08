namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Server.Models;
    using WatsonWebserver;

    /// <summary>
    /// A plain (tool-free) chat completion over a configured endpoint, for the dashboard chat surface. This
    /// is a conversational completion — it does not run the agent loop or expose tools.
    /// </summary>
    public sealed class ChatRoutes
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string? _ApiKey;
        private readonly Func<List<EndpointConfig>> _EndpointsProvider;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="endpointsProvider">Callback returning the configured endpoints.</param>
        public ChatRoutes(string? apiKey, Func<List<EndpointConfig>> endpointsProvider)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            app.Post("/v1.0/api/chat", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

                ChatRequest? request;
                try
                {
                    string body = req.Http.Request.DataAsString ?? string.Empty;
                    request = JsonSerializer.Deserialize<ChatRequest>(body, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (request == null || string.IsNullOrWhiteSpace(request.Endpoint) || request.Messages == null || request.Messages.Count == 0)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "'endpoint' and a non-empty 'messages' array are required.");
                }

                EndpointConfig? endpoint = _EndpointsProvider().FirstOrDefault(e => string.Equals(e.Name, request.Endpoint, StringComparison.Ordinal));
                if (endpoint == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return (object)new ApiError("NotFound", "Unknown endpoint: " + request.Endpoint);
                }

                bool ignoreCertErrors = false;
                try { ignoreCertErrors = SettingsLoader.LoadSettings().IgnoreCertErrors; } catch (Exception) { }

                List<ConversationMessage> messages = new List<ConversationMessage>();
                foreach (ChatMessageDto message in request.Messages)
                {
                    messages.Add(new ConversationMessage
                    {
                        Role = ParseRole(message.Role),
                        Content = message.Content ?? string.Empty
                    });
                }

                try
                {
                    using LlmClient client = new LlmClient(endpoint, ignoreCertErrors);
                    ConversationMessage reply = await client.SendAsync(messages, new List<ToolDefinition>(), req.Http.Token).ConfigureAwait(false);

                    req.Http.Response.StatusCode = 200;
                    return (object)new ChatReply
                    {
                        Role = "assistant",
                        Content = reply.Content ?? string.Empty,
                        Endpoint = endpoint.Name,
                        Model = endpoint.Model
                    };
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 502;
                    return (object)new ApiError("UpstreamError", "The model backend failed: " + ex.Message);
                }
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
    }
}
