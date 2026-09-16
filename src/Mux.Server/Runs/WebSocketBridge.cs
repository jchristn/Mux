namespace Mux.Server.Runs
{
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Runs;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;

    /// <summary>
    /// Bridges a WebSocket connection to a run's event stream. A client subscribes to a run (by run id or
    /// session id); the bridge replays the frames already emitted, then live-tails subsequent frames. Each
    /// frame is a canonical event envelope (identical to the headless JSONL contract), forwarded verbatim.
    /// Producers may also publish a run's frames into the hub over the same socket, and clients may answer
    /// tool-approval prompts.
    ///
    /// <para>The upgrade is authenticated: when an API key is configured it must be supplied via the
    /// <c>Authorization: Bearer</c> header or an <c>?apiKey=</c> query parameter (browsers cannot set the
    /// header on a WebSocket).</para>
    /// </summary>
    public sealed class WebSocketBridge
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly string? _ApiKey;
        private readonly RunRegistry _Runs;
        private readonly string _Version;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="apiKey">Configured API key, or null/empty for no-auth mode.</param>
        /// <param name="runs">The server's run registry.</param>
        /// <param name="version">Product version, announced on connect.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="runs"/> is null.</exception>
        public WebSocketBridge(string? apiKey, RunRegistry runs, string version)
        {
            _ApiKey = apiKey;
            _Runs = runs ?? throw new ArgumentNullException(nameof(runs));
            _Version = version ?? string.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handles a WebSocket session for the lifetime of the connection.
        /// </summary>
        /// <param name="ctx">The upgrade HTTP context.</param>
        /// <param name="session">The WebSocket session.</param>
        public async Task HandleAsync(HttpContextBase ctx, WebSocketSession session)
        {
            if (ctx == null || session == null) return;

            SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

            if (!Authorized(ctx))
            {
                await SafeSendAsync(session, sendLock, ErrorFrame("unauthorized", "Authentication required."), ctx.Token).ConfigureAwait(false);
                return;
            }

            await SafeSendAsync(session, sendLock, ConnectedFrame(), ctx.Token).ConfigureAwait(false);

            object gate = new object();
            List<RunHandle> attached = new List<RunHandle>();
            List<RunSubscription> subscriptions = new List<RunSubscription>();
            List<Task> pumps = new List<Task>();
            RunHandle? approvalTarget = null;
            bool subscribed = false;
            Action<RunHandle>? watcher = null;
            Action<string>? sessionsListener = null;

            void Attach(RunHandle handle, bool closeOnComplete)
            {
                RunSubscription sub = handle.Subscribe();
                lock (gate)
                {
                    attached.Add(handle);
                    subscriptions.Add(sub);
                    if (approvalTarget == null || !handle.IsTerminal)
                    {
                        approvalTarget = handle;
                    }

                    pumps.Add(Task.Run(() => PumpAsync(session, sendLock, sub, ctx.Token, closeOnComplete)));
                }
            }

            try
            {
                await foreach (WebSocketMessage message in session.ReadMessagesAsync(ctx.Token).ConfigureAwait(false))
                {
                    if (message.MessageType != WebSocketMessageType.Text || string.IsNullOrWhiteSpace(message.Text))
                    {
                        continue;
                    }

                    ClientFrame? frame = TryParse(message.Text);
                    if (frame == null)
                    {
                        continue;
                    }

                    string action = (frame.Action ?? string.Empty).ToLowerInvariant();
                    if (action == "notify")
                    {
                        // A surface signalled an out-of-band conversation-list change (rename/duplicate/delete).
                        _Runs.NotifySessionsChanged(frame.SessionId ?? string.Empty);
                    }
                    else if (action == "subscribe" && frame.All && sessionsListener == null)
                    {
                        // Global list-change notifications: refresh a surface's conversation list whenever any
                        // run completes or a surface signals a change. Independent of any run subscription.
                        sessionsListener = sid => { _ = SafeSendAsync(session, sendLock, SessionsChangedFrame(sid), ctx.Token); };
                        _Runs.AddSessionsListener(sessionsListener);
                    }
                    else if (action == "subscribe" && !subscribed)
                    {
                        if (!string.IsNullOrWhiteSpace(frame.RunId))
                        {
                            // Run-scoped: attach to exactly this run and close the socket when it ends.
                            if (_Runs.TryGet(frame.RunId!, out RunHandle? runHandle) && runHandle != null)
                            {
                                subscribed = true;
                                Attach(runHandle, closeOnComplete: true);
                            }
                            else
                            {
                                await SafeSendAsync(session, sendLock, ErrorFrame("not_found", "No run matched the subscription."), ctx.Token).ConfigureAwait(false);
                            }
                        }
                        else if (!string.IsNullOrWhiteSpace(frame.SessionId))
                        {
                            // Session-scoped ("mirror on by default"): attach to any run already in flight for
                            // the session, and to future runs as they start — without erroring when the session
                            // is idle. The socket stays open across runs until the client disconnects.
                            subscribed = true;
                            string targetSession = frame.SessionId!;
                            foreach (RunHandle existing in _Runs.FindBySession(targetSession))
                            {
                                Attach(existing, closeOnComplete: false);
                            }

                            watcher = h =>
                            {
                                if (string.Equals(h.SessionId, targetSession, StringComparison.Ordinal))
                                {
                                    Attach(h, closeOnComplete: false);
                                }
                            };
                            _Runs.RunRegistered += watcher;
                        }
                    }
                    else if (action == "approve")
                    {
                        RunHandle? target = approvalTarget;
                        target?.ResolveApproval(frame.ToolCallId ?? string.Empty, NormalizeDecision(frame.Decision));
                    }
                    else if (action == "publish" && !string.IsNullOrEmpty(frame.RunId))
                    {
                        // A producer (a run driven in another process) is streaming its events into this hub so
                        // subscribers on any surface can observe them. The first frame materializes the run.
                        RunHandle materialized = _Runs.GetOrCreate(frame.RunId!, frame.SessionId ?? string.Empty, frame.EndpointName ?? string.Empty, frame.Model ?? string.Empty);
                        if (!string.IsNullOrEmpty(frame.Frame))
                        {
                            materialized.ApplyEnvelope(frame.Frame);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Connection closed.
            }
            catch (Exception)
            {
                // Best-effort: a transport fault ends the session.
            }
            finally
            {
                if (watcher != null)
                {
                    _Runs.RunRegistered -= watcher;
                }

                if (sessionsListener != null)
                {
                    _Runs.RemoveSessionsListener(sessionsListener);
                }

                List<RunHandle> attachedSnapshot;
                List<RunSubscription> subscriptionsSnapshot;
                List<Task> pumpsSnapshot;
                lock (gate)
                {
                    attachedSnapshot = new List<RunHandle>(attached);
                    subscriptionsSnapshot = new List<RunSubscription>(subscriptions);
                    pumpsSnapshot = new List<Task>(pumps);
                }

                for (int i = 0; i < attachedSnapshot.Count && i < subscriptionsSnapshot.Count; i++)
                {
                    attachedSnapshot[i].Unsubscribe(subscriptionsSnapshot[i]);
                }

                foreach (Task pump in pumpsSnapshot)
                {
                    try { await pump.ConfigureAwait(false); } catch (Exception) { }
                }

                sendLock.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        // Replays the subscription's buffered frames, then live-tails until the run ends, forwarding each
        // frame verbatim. When closeOnComplete is true (a single run-scoped subscription) the socket is closed
        // once the run ends so the inbound read loop unblocks; when false (a session-scoped subscription that
        // may span several runs) the socket is left open for the next run.
        private async Task PumpAsync(WebSocketSession session, SemaphoreSlim sendLock, RunSubscription subscription, CancellationToken token, bool closeOnComplete)
        {
            try
            {
                foreach (string replayed in subscription.Replay)
                {
                    await SafeSendAsync(session, sendLock, replayed, token).ConfigureAwait(false);
                }

                while (await subscription.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (subscription.Reader.TryRead(out string? frame))
                    {
                        if (frame != null)
                        {
                            await SafeSendAsync(session, sendLock, frame, token).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Connection closed.
            }
            catch (Exception)
            {
                // Best-effort tail.
            }

            if (closeOnComplete)
            {
                try { await session.CloseAsync(WebSocketCloseStatus.NormalClosure, "run ended", token).ConfigureAwait(false); } catch (Exception) { }
            }
        }

        private bool Authorized(HttpContextBase ctx)
        {
            if (string.IsNullOrEmpty(_ApiKey))
            {
                return true;
            }

            string? auth = ctx.Request.Headers["Authorization"];
            if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                string provided = auth.Substring("Bearer ".Length).Trim();
                if (string.Equals(provided, _ApiKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            string? queryKey = ctx.Request.Query?.Elements?["apiKey"];
            return !string.IsNullOrEmpty(queryKey) && string.Equals(queryKey, _ApiKey, StringComparison.Ordinal);
        }

        private static async Task SafeSendAsync(WebSocketSession session, SemaphoreSlim sendLock, string text, CancellationToken token)
        {
            await sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await session.SendTextAsync(text, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort — the connection may already be gone.
            }
            finally
            {
                sendLock.Release();
            }
        }

        private static ClientFrame? TryParse(string text)
        {
            try
            {
                return JsonSerializer.Deserialize<ClientFrame>(text, _JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string NormalizeDecision(string? decision)
        {
            string verdict = (decision ?? "n").Trim().ToLowerInvariant();
            return verdict == "y" || verdict == "always" ? verdict : "n";
        }

        private string ConnectedFrame()
        {
            return "{\"eventType\":\"server.connected\",\"product\":\"mux\",\"version\":" + JsonSerializer.Serialize(_Version) + "}";
        }

        private static string SessionsChangedFrame(string sessionId)
        {
            return "{\"eventType\":\"sessions_changed\",\"sessionId\":" + JsonSerializer.Serialize(sessionId ?? string.Empty) + "}";
        }

        private static string ErrorFrame(string code, string message)
        {
            return "{\"eventType\":\"error\",\"code\":" + JsonSerializer.Serialize(code) + ",\"message\":" + JsonSerializer.Serialize(message) + "}";
        }

        #endregion
    }
}
