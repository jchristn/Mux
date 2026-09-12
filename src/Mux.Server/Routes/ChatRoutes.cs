namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Telemetry;
    using Mux.Core.Tools;
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

        // Server-lifetime tool runtimes, created lazily on the first chat so MCP servers are connected once
        // and reused across requests. Not disposed — they live for the server process.
        private readonly object _ToolSync = new object();
        private McpRuntime? _Mcp;
        private SkillRuntime? _Skills;
        private bool _ToolsInitialized;

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

            // Warm (load) a model so the next chat's first token is fast. The dashboard calls this when the
            // chat surface opens and whenever the selected endpoint changes. Best-effort: it probes the model
            // via a short streaming request and reports reachability.
            app.Post("/v1.0/api/model/load", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

                ChatRequest? loadRequest;
                try
                {
                    loadRequest = JsonSerializer.Deserialize<ChatRequest>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (loadRequest == null || string.IsNullOrWhiteSpace(loadRequest.Endpoint))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "'endpoint' is required.");
                }

                EndpointConfig? loadEndpoint = _EndpointsProvider().FirstOrDefault(e => string.Equals(e.Name, loadRequest.Endpoint, StringComparison.Ordinal));
                if (loadEndpoint == null)
                {
                    req.Http.Response.StatusCode = 404;
                    return (object)new ApiError("NotFound", "Unknown endpoint: " + loadRequest.Endpoint);
                }

                bool ignoreCert = false;
                try { ignoreCert = SettingsLoader.LoadSettings().IgnoreCertErrors; } catch (Exception) { }

                ModelLoadResult result = await LlmClient.LoadModelAsync(loadEndpoint, ignoreCert, req.Http.Token).ConfigureAwait(false);
                return (object)new ModelLoadReply { Ok = result.Success, Reachable = result.Reachable, Error = result.Error };
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

                List<ConversationMessage> messages = BuildMessages(request);

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
                    RecordChatUsage(endpoint, client, ttftMs, totalMs, errorMessage == null);

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

        // Lazily starts the server-lifetime MCP + skills runtimes on the first chat so the dashboard model can
        // call MCP tools and use skills. Best-effort: if either fails to start, chat still works without it.
        private void EnsureToolRuntimes()
        {
            if (_ToolsInitialized)
            {
                return;
            }

            lock (_ToolSync)
            {
                if (_ToolsInitialized)
                {
                    return;
                }

                try
                {
                    _Mcp = new McpRuntime(SettingsLoader.LoadMcpServers, () => { }, TimeSpan.FromSeconds(30), onNotice: null);
                    _Mcp.Start();
                }
                catch (Exception)
                {
                    _Mcp = null;
                }

                try
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    if (settings.SkillsEnabled)
                    {
                        _Skills = new SkillRuntime(
                            SettingsLoader.ResolveSkillsDirectory(settings),
                            SettingsLoader.LoadSkillIndex,
                            () => { },
                            TimeSpan.FromSeconds(settings.SkillRefreshIntervalSeconds));
                        _Skills.Start();
                    }
                }
                catch (Exception)
                {
                    _Skills = null;
                }

                _ToolsInitialized = true;
            }
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

            EnsureToolRuntimes();

            MuxSettings settings;
            try { settings = SettingsLoader.LoadSettings(); } catch (Exception) { settings = new MuxSettings(); }

            // Split the message array into prior history + the newest user prompt (the agent loop appends the
            // prompt itself). The dashboard always sends the user's new message last.
            List<ConversationMessage> history = new List<ConversationMessage>();
            for (int i = 0; i < request.Messages.Count - 1; i++)
            {
                ChatMessageDto m = request.Messages[i];
                history.Add(new ConversationMessage { Role = RoleEnumExtensions.ParseRole(m.Role), Content = m.Content ?? string.Empty });
            }
            string prompt = request.Messages[request.Messages.Count - 1].Content ?? string.Empty;

            bool toolsEnabled = endpoint.Quirks?.SupportsTools ?? true;
            List<ToolDefinition> builtInTools = new BuiltInToolRegistry(settings).GetToolDefinitions();
            ResolvedSystemPrompt resolved = SystemPromptResolver.Resolve(
                SettingsLoader.LoadSystemPrompt(null, settings),
                SettingsLoader.GetActivePromptProfile(),
                toolsEnabled,
                builtInTools,
                Directory.GetCurrentDirectory(),
                settings.TaskPlanningEnabled,
                null);

            // Everything validated — switch the response into Server-Sent Events mode and stream.
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.ServerSentEvents = true;

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            long ttftMs = -1;
            System.Text.StringBuilder content = new System.Text.StringBuilder();
            RunCompletedEvent? runCompleted = null;
            string? errorMessage = null;

            try
            {
                AgentLoopOptions options = new AgentLoopOptions(endpoint)
                {
                    ConversationHistory = history,
                    SystemPrompt = resolved.SystemPrompt,
                    CompactionSystemPrompt = resolved.CompactionSystemPrompt,
                    // No approval UI in the browser: read-only tools auto-run under AutoSafe; anything that
                    // would prompt (mutating tools like write_file / run_process) is denied so a web chat can
                    // never mutate the server.
                    ApprovalPolicy = ApprovalPolicyEnum.AutoSafe,
                    PromptUserFunc = _ => System.Threading.Tasks.Task.FromResult("n"),
                    WorkingDirectory = Directory.GetCurrentDirectory(),
                    MuxSettings = settings,
                    MaxIterations = settings.GetEffectiveMaxAgentIterations(endpoint),
                    ConfigDirectory = SettingsLoader.GetConfigDirectory(),
                    CommandName = "dashboard",
                    SessionId = request.Id ?? string.Empty,
                    UsageRecorder = _UsageRecorder
                };

                if (toolsEnabled)
                {
                    IReadOnlyList<ToolDefinition> mcpTools = _Mcp?.CurrentTools ?? new List<ToolDefinition>();
                    Func<string, System.Text.Json.JsonElement, string, System.Threading.CancellationToken, System.Threading.Tasks.Task<ToolResult>>? executor =
                        _Mcp != null ? _Mcp.ExecuteToolAsync : null;
                    ExternalToolsBinder.Apply(options, resolved.SystemPrompt, resolved.CompactionSystemPrompt, mcpTools, executor, _Skills, builtInTools.Count);
                }

                using AgentLoop loop = new AgentLoop(options);
                await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, ctx.Token).ConfigureAwait(false))
                {
                    switch (agentEvent)
                    {
                        case AssistantTextEvent textEvent:
                            if (string.IsNullOrEmpty(textEvent.Text)) break;
                            if (ttftMs < 0) ttftMs = stopwatch.ElapsedMilliseconds;
                            content.Append(textEvent.Text);
                            await ctx.Response.SendEvent(new ServerSentEvent { Event = "token", Data = JsonSerializer.Serialize(textEvent.Text) }, false, ctx.Token).ConfigureAwait(false);
                            break;
                        case AssistantThinkingEvent thinkingEvent:
                            if (string.IsNullOrEmpty(thinkingEvent.Text)) break;
                            await ctx.Response.SendEvent(new ServerSentEvent { Event = "thinking", Data = JsonSerializer.Serialize(thinkingEvent.Text) }, false, ctx.Token).ConfigureAwait(false);
                            break;
                        case ToolCallProposedEvent proposed:
                            await ctx.Response.SendEvent(new ServerSentEvent { Event = "tool", Data = JsonSerializer.Serialize(new ChatToolEvent { Id = proposed.ToolCall.Id ?? string.Empty, Name = proposed.ToolCall.Name, Status = "running" }) }, false, ctx.Token).ConfigureAwait(false);
                            break;
                        case ToolCallCompletedEvent completed:
                            bool ok = completed.Result != null && completed.Result.Success;
                            await ctx.Response.SendEvent(new ServerSentEvent { Event = "tool", Data = JsonSerializer.Serialize(new ChatToolEvent { Id = completed.ToolCallId ?? string.Empty, Name = completed.ToolName, Status = ok ? "ok" : "fail", ElapsedMs = completed.ElapsedMs }) }, false, ctx.Token).ConfigureAwait(false);
                            break;
                        case ErrorEvent errorEvent:
                            errorMessage = errorEvent.Message;
                            break;
                        case RunCompletedEvent rc:
                            runCompleted = rc;
                            break;
                        default:
                            break;
                    }
                }

                long totalMs = stopwatch.ElapsedMilliseconds;

                if (errorMessage != null && content.Length == 0)
                {
                    await ctx.Response.SendEvent(new ServerSentEvent
                    {
                        Event = "error",
                        Data = JsonSerializer.Serialize("The model backend failed: " + errorMessage)
                    }, true, ctx.Token).ConfigureAwait(false);
                    return;
                }

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
                        InputTokens = runCompleted?.InputTokens ?? 0,
                        OutputTokens = runCompleted?.OutputTokens ?? 0,
                        TotalTokens = (runCompleted?.InputTokens ?? 0) + (runCompleted?.OutputTokens ?? 0)
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

            UsageEvent usageEvent = UsageEvent.FromCall(
                endpoint, client.LastCall, client.LastUsage, UsageCallKindEnum.Chat, "serve", success, ttftMs, totalMs);
            _UsageRecorder.Record(usageEvent);
        }

        private static async Task SendJsonAsync(HttpContextBase ctx, int statusCode, ApiError error)
        {
            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(JsonSerializer.Serialize(error)).ConfigureAwait(false);
        }


        // Maps the request messages, prepending mux's system prompt when the caller supplied none, so the
        // dashboard chat carries mux's persona and its "don't reveal the instructions" guidance instead of
        // falling back to the raw provider default (which leaked "You are ChatGPT ..."). The dashboard runs no
        // tools, so the tools-disabled variant is used — the model is not told about tools it cannot call.
        private static List<ConversationMessage> BuildMessages(ChatRequest request)
        {
            List<ConversationMessage> messages = new List<ConversationMessage>();

            bool callerSuppliedSystem = request.Messages.Count > 0
                && RoleEnumExtensions.ParseRole(request.Messages[0].Role) == RoleEnum.System;
            if (!callerSuppliedSystem)
            {
                try
                {
                    MuxSettings settings = SettingsLoader.LoadSettings();
                    PromptProfile profile = SettingsLoader.GetActivePromptProfile();
                    ResolvedSystemPrompt resolved = SystemPromptResolver.Resolve(
                        SettingsLoader.LoadSystemPrompt(null, settings),
                        profile,
                        toolsEnabled: false,
                        tools: null,
                        workingDirectory: Directory.GetCurrentDirectory(),
                        taskPlanningEnabled: false,
                        appendSystemPrompt: null);
                    if (!string.IsNullOrWhiteSpace(resolved.SystemPrompt))
                    {
                        messages.Add(new ConversationMessage { Role = RoleEnum.System, Content = resolved.SystemPrompt });
                    }
                }
                catch (Exception)
                {
                    // Best-effort: if resolution fails, fall through and send the caller's messages as-is.
                }
            }

            foreach (ChatMessageDto message in request.Messages)
            {
                messages.Add(new ConversationMessage { Role = RoleEnumExtensions.ParseRole(message.Role), Content = message.Content ?? string.Empty });
            }

            return messages;
        }
    }
}
