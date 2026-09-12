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
    using Mux.Core.Telemetry;
    using Mux.Server.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

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
        private readonly IUsageRecorder? _UsageRecorder;

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null for no-auth.</param>
        /// <param name="endpointsProvider">Callback returning the configured endpoints.</param>
        /// <param name="usageRecorder">Optional recorder so the server's chat calls are captured. Null skips recording.</param>
        public ChatRoutes(string? apiKey, Func<List<EndpointConfig>> endpointsProvider, IUsageRecorder? usageRecorder = null)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _UsageRecorder = usageRecorder;
        }

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="app">Watson webserver.</param>
        public void Register(Webserver app)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            // Streaming chat over Server-Sent Events: tokens are pushed to the browser as the model produces
            // them (a "token" event per delta), followed by a terminal "done" event carrying the final stats.
            // The handler streams directly onto the response and marks it sent, so the framework's
            // ResponseSent guard skips serializing the (null) return value.
            app.Post("/v1.0/api/chat/stream", async (req) =>
            {
                await StreamChatAsync(req.Http).ConfigureAwait(false);
                return (object?)null;
            });

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

                    // Drive the streaming API so we can measure real time-to-first-token and streaming
                    // duration, then return the buffered text plus stats in a single JSON reply (no SSE
                    // needed on the wire). Token counts come from the provider-reported usage recorded at
                    // the end of the stream.
                    System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    long ttftMs = -1;
                    System.Text.StringBuilder content = new System.Text.StringBuilder();
                    string? errorMessage = null;

                    await foreach (Mux.Core.Agent.AgentEvent agentEvent in client.StreamAsync(messages, new List<ToolDefinition>(), req.Http.Token).ConfigureAwait(false))
                    {
                        if (agentEvent is Mux.Core.Agent.AssistantTextEvent textEvent)
                        {
                            if (ttftMs < 0)
                            {
                                ttftMs = stopwatch.ElapsedMilliseconds;
                            }

                            content.Append(textEvent.Text);
                        }
                        else if (agentEvent is Mux.Core.Agent.ErrorEvent errorEvent)
                        {
                            errorMessage = errorEvent.Message;
                        }
                    }

                    long totalMs = stopwatch.ElapsedMilliseconds;

                    if (errorMessage != null && content.Length == 0)
                    {
                        req.Http.Response.StatusCode = 502;
                        return (object)new ApiError("UpstreamError", "The model backend failed: " + errorMessage);
                    }

                    Mux.Core.Llm.LlmUsage? usage = client.LastUsage;

                    // Record durable usage telemetry for the server's own chat call (best-effort).
                    if (_UsageRecorder != null)
                    {
                        LlmCallMetrics? call = client.LastCall;
                        UsageEvent usageEvent = new UsageEvent
                        {
                            CallKind = UsageCallKindEnum.Chat,
                            Command = "serve",
                            EndpointName = endpoint.Name,
                            AdapterType = endpoint.AdapterType.ToString(),
                            Model = string.IsNullOrEmpty(call?.Model) ? endpoint.Model : call!.Model!,
                            BaseHost = TryHost(endpoint.BaseUrl),
                            InputTokens = usage?.InputTokens ?? 0,
                            CachedTokens = usage?.CachedTokens ?? 0,
                            OutputTokens = usage?.OutputTokens ?? 0,
                            ReasoningTokens = usage?.ReasoningTokens ?? 0,
                            TotalTokens = usage?.TotalTokens ?? 0,
                            TimeToFirstTokenMs = call?.TimeToFirstTokenMs ?? (ttftMs >= 0 ? ttftMs : (long?)null),
                            StreamingMs = call?.StreamingMs ?? (ttftMs >= 0 ? System.Math.Max(0, totalMs - ttftMs) : (long?)null),
                            TotalMs = call?.TotalMs ?? totalMs,
                            TokensPerSecond = call?.TokensPerSecond,
                            FinishReason = call?.FinishReason,
                            Success = errorMessage == null
                        };
                        _UsageRecorder.Record(usageEvent);
                    }

                    req.Http.Response.StatusCode = 200;
                    return (object)new ChatReply
                    {
                        Role = "assistant",
                        Content = content.ToString(),
                        Endpoint = endpoint.Name,
                        Model = endpoint.Model,
                        Stats = new ChatStats
                        {
                            TtftMs = ttftMs,
                            StreamingMs = ttftMs >= 0 ? System.Math.Max(0, totalMs - ttftMs) : 0,
                            TotalMs = totalMs,
                            InputTokens = usage?.InputTokens ?? 0,
                            OutputTokens = usage?.OutputTokens ?? 0,
                            TotalTokens = usage?.TotalTokens ?? 0
                        }
                    };
                }
                catch (Exception ex)
                {
                    req.Http.Response.StatusCode = 502;
                    return (object)new ApiError("UpstreamError", "The model backend failed: " + ex.Message);
                }
            });
        }

        private async Task StreamChatAsync(HttpContextBase ctx)
        {
            if (!ApiAuth.Authorize(ctx, _ApiKey))
            {
                await SendJsonAsync(ctx, 401, new ApiError("Unauthorized", "Authentication required.")).ConfigureAwait(false);
                return;
            }

            ChatRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<ChatRequest>(ctx.Request.DataAsString ?? string.Empty, _JsonOptions);
            }
            catch (Exception)
            {
                await SendJsonAsync(ctx, 400, new ApiError("BadRequest", "Request body is not valid JSON.")).ConfigureAwait(false);
                return;
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Endpoint) || request.Messages == null || request.Messages.Count == 0)
            {
                await SendJsonAsync(ctx, 400, new ApiError("BadRequest", "'endpoint' and a non-empty 'messages' array are required.")).ConfigureAwait(false);
                return;
            }

            EndpointConfig? endpoint = _EndpointsProvider().FirstOrDefault(e => string.Equals(e.Name, request.Endpoint, StringComparison.Ordinal));
            if (endpoint == null)
            {
                await SendJsonAsync(ctx, 404, new ApiError("NotFound", "Unknown endpoint: " + request.Endpoint)).ConfigureAwait(false);
                return;
            }

            bool ignoreCertErrors = false;
            try { ignoreCertErrors = SettingsLoader.LoadSettings().IgnoreCertErrors; } catch (Exception) { }

            List<ConversationMessage> messages = new List<ConversationMessage>();
            foreach (ChatMessageDto message in request.Messages)
            {
                messages.Add(new ConversationMessage { Role = ParseRole(message.Role), Content = message.Content ?? string.Empty });
            }

            // Everything validated — switch the response into Server-Sent Events mode and stream.
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.ServerSentEvents = true;

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long ttftMs = -1;
            System.Text.StringBuilder content = new System.Text.StringBuilder();
            string? errorMessage = null;

            try
            {
                using LlmClient client = new LlmClient(endpoint, ignoreCertErrors);

                await foreach (Mux.Core.Agent.AgentEvent agentEvent in client.StreamAsync(messages, new List<ToolDefinition>(), ctx.Token).ConfigureAwait(false))
                {
                    if (agentEvent is Mux.Core.Agent.AssistantTextEvent textEvent)
                    {
                        if (string.IsNullOrEmpty(textEvent.Text))
                        {
                            continue;
                        }

                        if (ttftMs < 0)
                        {
                            ttftMs = stopwatch.ElapsedMilliseconds;
                        }

                        content.Append(textEvent.Text);
                        await ctx.Response.SendEvent(new ServerSentEvent
                        {
                            Event = "token",
                            Data = JsonSerializer.Serialize(textEvent.Text)
                        }, false, ctx.Token).ConfigureAwait(false);
                    }
                    else if (agentEvent is Mux.Core.Agent.ErrorEvent errorEvent)
                    {
                        errorMessage = errorEvent.Message;
                    }
                }

                long totalMs = stopwatch.ElapsedMilliseconds;
                RecordChatUsage(endpoint, client, ttftMs, totalMs, errorMessage == null);

                if (errorMessage != null && content.Length == 0)
                {
                    await ctx.Response.SendEvent(new ServerSentEvent
                    {
                        Event = "error",
                        Data = JsonSerializer.Serialize("The model backend failed: " + errorMessage)
                    }, true, ctx.Token).ConfigureAwait(false);
                    return;
                }

                Mux.Core.Llm.LlmUsage? usage = client.LastUsage;
                ChatReply reply = new ChatReply
                {
                    Role = "assistant",
                    Content = content.ToString(),
                    Endpoint = endpoint.Name,
                    Model = endpoint.Model,
                    Stats = new ChatStats
                    {
                        TtftMs = ttftMs,
                        StreamingMs = ttftMs >= 0 ? System.Math.Max(0, totalMs - ttftMs) : 0,
                        TotalMs = totalMs,
                        InputTokens = usage?.InputTokens ?? 0,
                        OutputTokens = usage?.OutputTokens ?? 0,
                        TotalTokens = usage?.TotalTokens ?? 0
                    }
                };

                await ctx.Response.SendEvent(new ServerSentEvent
                {
                    Event = "done",
                    Data = JsonSerializer.Serialize(reply)
                }, true, ctx.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The client disconnected; nothing more to send.
            }
            catch (Exception ex)
            {
                try
                {
                    await ctx.Response.SendEvent(new ServerSentEvent
                    {
                        Event = "error",
                        Data = JsonSerializer.Serialize("The model backend failed: " + ex.Message)
                    }, true, ctx.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Best-effort — the connection may already be gone.
                }
            }
        }

        // Records durable usage telemetry for a server chat call (best-effort; shared by the buffered and
        // streaming routes).
        private void RecordChatUsage(EndpointConfig endpoint, LlmClient client, long ttftMs, long totalMs, bool success)
        {
            if (_UsageRecorder == null)
            {
                return;
            }

            Mux.Core.Llm.LlmUsage? usage = client.LastUsage;
            LlmCallMetrics? call = client.LastCall;
            UsageEvent usageEvent = new UsageEvent
            {
                CallKind = UsageCallKindEnum.Chat,
                Command = "serve",
                EndpointName = endpoint.Name,
                AdapterType = endpoint.AdapterType.ToString(),
                Model = string.IsNullOrEmpty(call?.Model) ? endpoint.Model : call!.Model!,
                BaseHost = TryHost(endpoint.BaseUrl),
                InputTokens = usage?.InputTokens ?? 0,
                CachedTokens = usage?.CachedTokens ?? 0,
                OutputTokens = usage?.OutputTokens ?? 0,
                ReasoningTokens = usage?.ReasoningTokens ?? 0,
                TotalTokens = usage?.TotalTokens ?? 0,
                TimeToFirstTokenMs = call?.TimeToFirstTokenMs ?? (ttftMs >= 0 ? ttftMs : (long?)null),
                StreamingMs = call?.StreamingMs ?? (ttftMs >= 0 ? System.Math.Max(0, totalMs - ttftMs) : (long?)null),
                TotalMs = call?.TotalMs ?? totalMs,
                TokensPerSecond = call?.TokensPerSecond,
                FinishReason = call?.FinishReason,
                Success = success
            };
            _UsageRecorder.Record(usageEvent);
        }

        private static async Task SendJsonAsync(HttpContextBase ctx, int statusCode, ApiError error)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(JsonSerializer.Serialize(error)).ConfigureAwait(false);
        }

        private static string? TryHost(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return null;
            }

            return Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri) && !string.IsNullOrEmpty(uri.Host) ? uri.Host : null;
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
