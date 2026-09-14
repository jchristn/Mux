namespace Mux.Server.Routes
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Prompting;
    using Mux.Core.Sessions;
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
        private readonly SessionStore? _SessionStore;
        private readonly bool _AllowInteractiveTools;
        private readonly CheckpointRegistry? _Checkpoints;

        // Pending tool approvals for interactive web chats, keyed by "runId:toolCallId". The streaming run
        // registers a completion source and streams an "approval" event; the browser answers via
        // POST /v1.0/api/chat/approve, which resolves the source. Server-lifetime.
        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _PendingApprovals =
            new ConcurrentDictionary<string, TaskCompletionSource<string>>();

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
        /// <param name="sessionStore">Optional session store so streamed web chats are persisted server-side (into the shared session store) keyed by session id. Null disables server-side persistence.</param>
        /// <param name="allowInteractiveTools">When true, mutating tools proposed during a web chat prompt the browser for approval instead of being auto-denied. Defaults to false (read-only web chat).</param>
        /// <param name="checkpoints">Optional checkpoint registry so each run records a pre-turn git snapshot for undo/redo. Null disables checkpointing.</param>
        public ChatRoutes(
            string? apiKey,
            Func<List<EndpointConfig>> endpointsProvider,
            IUsageRecorder? usageRecorder = null,
            SessionStore? sessionStore = null,
            bool allowInteractiveTools = false,
            CheckpointRegistry? checkpoints = null)
        {
            _ApiKey = apiKey;
            _EndpointsProvider = endpointsProvider ?? throw new ArgumentNullException(nameof(endpointsProvider));
            _UsageRecorder = usageRecorder;
            _SessionStore = sessionStore;
            _AllowInteractiveTools = allowInteractiveTools;
            _Checkpoints = checkpoints;
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

            // Answer an approval prompt raised during an interactive web chat run. The streaming run is
            // blocked awaiting this decision; resolving the pending completion source unblocks it.
            app.Post("/v1.0/api/chat/approve", async (req) =>
            {
                if (!ApiAuth.Authorize(req.Http, _ApiKey))
                {
                    return (object)new ApiError("Unauthorized", "Authentication required.");
                }

                ChatApproveRequest? decision;
                try
                {
                    decision = JsonSerializer.Deserialize<ChatApproveRequest>(req.Http.Request.DataAsString ?? string.Empty, _JsonOptions);
                }
                catch (Exception)
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "Request body is not valid JSON.");
                }

                if (decision == null || string.IsNullOrWhiteSpace(decision.RunId) || string.IsNullOrWhiteSpace(decision.ToolCallId))
                {
                    req.Http.Response.StatusCode = 400;
                    return (object)new ApiError("BadRequest", "'runId' and 'toolCallId' are required.");
                }

                string key = decision.RunId + ":" + decision.ToolCallId;
                if (_PendingApprovals.TryRemove(key, out TaskCompletionSource<string>? source))
                {
                    string verdict = (decision.Decision ?? "n").Trim().ToLowerInvariant();
                    if (verdict != "y" && verdict != "always")
                    {
                        verdict = "n";
                    }

                    source.TrySetResult(verdict);
                    req.Http.Response.StatusCode = 200;
                    return (object)new { ok = true };
                }

                req.Http.Response.StatusCode = 404;
                return (object)new ApiError("NotFound", "No pending approval for that run and tool call.");
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

            // Resolve the session id up front (minting one when the caller sent none) so the run is tagged,
            // the server can persist it, and the caller can adopt it from the terminal "done" event.
            string sessionId = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id!.Trim();

            // Resolve the directory the run's tools execute in: an explicit request value (which must exist),
            // else the persisted session's working directory, else the server's current directory. This lets
            // an editor or automation run mux against a specific workspace rather than wherever the server was
            // launched. Validated before switching to SSE so a bad path returns a clean 400, not a stream.
            string runDirectory = Directory.GetCurrentDirectory();
            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                if (!Directory.Exists(request.WorkingDirectory))
                {
                    await SendJsonAsync(ctx, 400, new ApiError("BadRequest", "workingDirectory does not exist: " + request.WorkingDirectory)).ConfigureAwait(false);
                    return;
                }

                runDirectory = request.WorkingDirectory!;
            }
            else if (_SessionStore != null)
            {
                try
                {
                    SessionSnapshot? existingSession = await _SessionStore.LoadAsync(sessionId, ctx.Token).ConfigureAwait(false);
                    if (existingSession != null && !string.IsNullOrWhiteSpace(existingSession.WorkingDirectory) && Directory.Exists(existingSession.WorkingDirectory))
                    {
                        runDirectory = existingSession.WorkingDirectory;
                    }
                }
                catch (Exception)
                {
                    // Fall back to the server's current directory.
                }
            }

            ResolvedSystemPrompt resolved = SystemPromptResolver.Resolve(
                SettingsLoader.LoadSystemPrompt(null, settings),
                SettingsLoader.GetActivePromptProfile(),
                toolsEnabled,
                builtInTools,
                runDirectory,
                settings.TaskPlanningEnabled,
                null);

            // A per-run id correlating interactive approval prompts with their decisions.
            string runId = Guid.NewGuid().ToString("N");

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
                    // Read-only tools auto-run under AutoSafe. By default anything that would prompt (mutating
                    // tools like write_file / run_process) is denied so a web chat can never mutate the host.
                    // When the server was started with interactive web tools enabled, such tools instead
                    // prompt the browser for approval over the SSE channel.
                    ApprovalPolicy = ApprovalPolicyEnum.AutoSafe,
                    PromptUserFunc = _AllowInteractiveTools
                        ? toolCall => RequestBrowserApprovalAsync(ctx, runId, toolCall)
                        : (Func<ToolCall, Task<string>>)(_ => Task.FromResult("n")),
                    WorkingDirectory = runDirectory,
                    MuxSettings = settings,
                    MaxIterations = settings.GetEffectiveMaxAgentIterations(endpoint),
                    ConfigDirectory = SettingsLoader.GetConfigDirectory(),
                    CommandName = "dashboard",
                    SessionId = sessionId,
                    UsageRecorder = _UsageRecorder
                };

                if (toolsEnabled)
                {
                    IReadOnlyList<ToolDefinition> mcpTools = _Mcp?.CurrentTools ?? new List<ToolDefinition>();
                    Func<string, System.Text.Json.JsonElement, string, System.Threading.CancellationToken, System.Threading.Tasks.Task<ToolResult>>? executor =
                        _Mcp != null ? _Mcp.ExecuteToolAsync : null;
                    ExternalToolsBinder.Apply(options, resolved.SystemPrompt, resolved.CompactionSystemPrompt, mcpTools, executor, _Skills, builtInTools.Count);
                }

                // Snapshot the working tree before the turn so an editor can undo the turn's file changes,
                // matching the TUI and desktop. Best-effort and only in a git repository; a checkpoint is a
                // shadow ref that never touches the user's branch, history, or stash.
                if (_Checkpoints != null)
                {
                    try
                    {
                        Mux.Core.Checkpoints.CheckpointManager? checkpointManager = await _Checkpoints.GetOrCreateAsync(runDirectory, ctx.Token).ConfigureAwait(false);
                        if (checkpointManager != null)
                        {
                            await checkpointManager.RecordAsync(CheckpointLabel(prompt), ctx.Token).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        // Checkpointing is best-effort; a failure must not block the run.
                    }
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

                // Persist the turn server-side into the shared session store so a web-started conversation is
                // durable without depending on a follow-up PUT from the browser, and appears (and is
                // resumable) on every surface. Best-effort and non-cancellable (the client may have already
                // disconnected once the run finished).
                await PersistTurnAsync(sessionId, endpoint, runDirectory, history, prompt, content.ToString()).ConfigureAwait(false);

                ChatReply reply = new ChatReply
                {
                    Role = "assistant",
                    Id = sessionId,
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

        // Persists a completed web-chat turn into the shared session store (best-effort). Loads the existing
        // snapshot first so prior turns' full-fidelity history (including tool calls) is preserved, then
        // appends the new user prompt and assistant reply. Non-cancellable — the browser may have already
        // disconnected once the run finished, but the turn must still be saved.
        private async Task PersistTurnAsync(
            string sessionId,
            EndpointConfig endpoint,
            string workingDirectory,
            List<ConversationMessage> priorFromRequest,
            string prompt,
            string assistantText)
        {
            if (_SessionStore == null || string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            try
            {
                SessionSnapshot? existing = await _SessionStore.LoadAsync(sessionId, CancellationToken.None).ConfigureAwait(false);
                DateTime now = DateTime.UtcNow;
                SessionSnapshot snapshot = existing ?? new SessionSnapshot { Id = sessionId, CreatedUtc = now };
                snapshot.Id = sessionId;

                // Prefer the stored history (preserves tool-call structure from prior turns); fall back to the
                // request's prior messages only for a brand-new session.
                List<ConversationMessage> prior = existing != null && existing.ConversationHistory.Count > 0
                    ? existing.ConversationHistory
                    : priorFromRequest;

                List<ConversationMessage> updated = new List<ConversationMessage>(prior)
                {
                    new ConversationMessage { Role = RoleEnum.User, Content = prompt },
                    new ConversationMessage { Role = RoleEnum.Assistant, Content = assistantText }
                };

                snapshot.ConversationHistory = updated;
                snapshot.EndpointName = endpoint.Name;
                snapshot.Model = endpoint.Model;
                snapshot.UpdatedUtc = now;
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    snapshot.WorkingDirectory = workingDirectory;
                }

                if (!snapshot.TitlePinned && string.IsNullOrWhiteSpace(snapshot.Title))
                {
                    snapshot.Title = SessionTitleHelper.Normalize(prompt, SessionTitleHelper.DefaultTitle);
                }

                await _SessionStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort persistence.
            }
        }

        // Streams an approval prompt to the browser and blocks the run until the browser answers (via
        // POST /v1.0/api/chat/approve), the request is cancelled, or a timeout elapses. Returns "y"/"always"
        // to approve or "n" to deny. Only used when the server was started with interactive web tools enabled.
        private async Task<string> RequestBrowserApprovalAsync(HttpContextBase ctx, string runId, ToolCall toolCall)
        {
            string toolCallId = toolCall?.Id ?? string.Empty;
            string key = runId + ":" + toolCallId;
            TaskCompletionSource<string> source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _PendingApprovals[key] = source;

            try
            {
                await ctx.Response.SendEvent(new ServerSentEvent
                {
                    Event = "approval",
                    Data = JsonSerializer.Serialize(new ChatApprovalRequest
                    {
                        RunId = runId,
                        ToolCallId = toolCallId,
                        Name = toolCall?.Name ?? string.Empty,
                        Arguments = toolCall?.Arguments ?? string.Empty
                    })
                }, false, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _PendingApprovals.TryRemove(key, out _);
                return "n";
            }

            try
            {
                Task delay = Task.Delay(TimeSpan.FromMinutes(5), ctx.Token);
                Task finished = await Task.WhenAny(source.Task, delay).ConfigureAwait(false);
                if (finished == source.Task)
                {
                    return source.Task.Result;
                }
            }
            catch (Exception)
            {
                // Fall through to deny on cancellation/timeout.
            }
            finally
            {
                _PendingApprovals.TryRemove(key, out _);
            }

            return "n";
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

        // Builds a short checkpoint label from the user's prompt.
        private static string CheckpointLabel(string prompt)
        {
            string trimmed = (prompt ?? string.Empty).Trim().Replace('\n', ' ');
            return trimmed.Length <= 60 ? trimmed : trimmed.Substring(0, 60);
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
