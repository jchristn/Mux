namespace Mux.Core.Runs
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Subscribes to a session on a mux hub over the WebSocket bridge and surfaces its live run frames, so a
    /// surface that drives the engine in its own process (the desktop app, the terminal) can also *consume* —
    /// reflecting runs started on any other surface. Complements <see cref="RunPublisher"/> (the producing
    /// half). Best-effort: a missing or unreachable hub simply yields no frames. Callbacks fire on a background
    /// read loop, so a UI consumer must marshal them to its own thread.
    /// </summary>
    public sealed class SessionMirrorClient : IAsyncDisposable
    {
        #region Private-Members

        private readonly string _BaseUrl;
        private readonly string? _ApiKey;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();

        private ClientWebSocket? _Socket;
        private Task? _ReceiveLoop;
        private string _SessionId = string.Empty;
        private bool _All;
        private TimeSpan _ReconnectDelay = TimeSpan.FromSeconds(2);
        private bool _Disposed;

        #endregion

        #region Public-Events

        /// <summary>Raised for every canonical envelope frame received (the raw JSON string).</summary>
        public event Action<string>? FrameReceived;

        /// <summary>Raised when a <c>run_completed</c> frame arrives — a run for the session finished.</summary>
        public event Action? RunCompleted;

        /// <summary>Raised when a <c>transcript_changed</c> frame arrives — the session's persisted transcript
        /// changed (a turn was appended by any surface, including one persisted without a run in the hub's
        /// registry). A viewer reloads the open transcript on this the same way it does on
        /// <see cref="RunCompleted"/>.</summary>
        public event Action? TranscriptChanged;

        /// <summary>Raised (with the affected session id, possibly empty) when the hub signals that the
        /// conversation list changed. Only fires when subscribed via <see cref="StartAllAsync"/>.</summary>
        public event Action<string>? SessionsChanged;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a session mirror client targeting a hub.
        /// </summary>
        /// <param name="baseUrl">The hub base URL, for example <c>http://127.0.0.1:8710</c>.</param>
        /// <param name="apiKey">The hub API key, or null when it runs without one.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is null or empty.</exception>
        public SessionMirrorClient(string baseUrl, string? apiKey)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _ApiKey = apiKey;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts a resilient background loop that connects to the hub, subscribes to the session, reads
        /// frames, and — critically — <b>reconnects</b> if the hub is not yet listening or the socket drops,
        /// retrying every <see cref="_ReconnectDelay"/> until the client is disposed. Non-blocking. Best-effort:
        /// while no hub is reachable, no frames are raised, but the moment one appears the subscription
        /// re-establishes itself.
        /// </summary>
        /// <param name="sessionId">The session to mirror. Must not be null or empty.</param>
        /// <param name="cancellationToken">Unused beyond validation; the client's own token governs its lifetime.</param>
        /// <returns>A completed task; the connection runs in the background.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="sessionId"/> is null or empty.</exception>
        public Task StartAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("Session id is required.", nameof(sessionId));

            _SessionId = sessionId;
            _ReceiveLoop = Task.Run(() => ConnectLoopAsync(_Cts.Token));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts a resilient global subscription that raises <see cref="SessionsChanged"/> whenever the hub
        /// reports a conversation-list change (any run completing, or a rename/delete signalled by a surface).
        /// A surface uses this to refresh its conversation list live. Non-blocking; reconnects like
        /// <see cref="StartAsync"/>.
        /// </summary>
        /// <param name="cancellationToken">Unused beyond the client's own lifetime token.</param>
        /// <returns>A completed task; the connection runs in the background.</returns>
        public Task StartAllAsync(CancellationToken cancellationToken)
        {
            _All = true;
            _ReceiveLoop = Task.Run(() => ConnectLoopAsync(_Cts.Token));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Signals the hub that this surface changed the conversation list out of band (a rename, duplicate,
        /// or delete) so other surfaces refresh. Best-effort — a no-op when not connected.
        /// </summary>
        /// <param name="sessionId">The affected session id, or empty.</param>
        /// <param name="cancellationToken">A token to cancel the send.</param>
        public async Task NotifySessionsChangedAsync(string sessionId, CancellationToken cancellationToken)
        {
            ClientWebSocket? socket = _Socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                string frame = "{\"action\":\"notify\",\"sessionId\":" + JsonSerializer.Serialize(sessionId ?? string.Empty) + "}";
                await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        /// <summary>
        /// Signals the hub that this surface appended a turn to a session's transcript, so every other surface
        /// viewing that session reloads it. Sent over the same socket the client already holds. Best-effort — a
        /// no-op when not connected.
        /// </summary>
        /// <param name="sessionId">The session whose transcript changed.</param>
        /// <param name="cancellationToken">A token to cancel the send.</param>
        public async Task NotifyTranscriptChangedAsync(string sessionId, CancellationToken cancellationToken)
        {
            ClientWebSocket? socket = _Socket;
            if (socket == null || socket.State != WebSocketState.Open || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            try
            {
                string frame = "{\"action\":\"notify-transcript\",\"sessionId\":" + JsonSerializer.Serialize(sessionId) + "}";
                await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best-effort.
            }
        }

        /// <summary>Stops the subscription and releases the socket. Safe to call more than once.</summary>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed) return;
            _Disposed = true;

            try { _Cts.Cancel(); } catch (ObjectDisposedException) { }

            ClientWebSocket? socket = _Socket;
            _Socket = null;
            if (socket != null)
            {
                try
                {
                    if (socket.State == WebSocketState.Open)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    // Best-effort close.
                }

                socket.Dispose();
            }

            if (_ReceiveLoop != null)
            {
                try { await _ReceiveLoop.ConfigureAwait(false); } catch (Exception) { }
            }

            try { _Cts.Dispose(); } catch (ObjectDisposedException) { }
        }

        #endregion

        #region Private-Methods

        private async Task ConnectLoopAsync(CancellationToken token)
        {
            string subscribe = _All
                ? "{\"action\":\"subscribe\",\"all\":true}"
                : "{\"action\":\"subscribe\",\"sessionId\":" + JsonSerializer.Serialize(_SessionId) + "}";
            while (!token.IsCancellationRequested)
            {
                ClientWebSocket socket = new ClientWebSocket();
                try
                {
                    await socket.ConnectAsync(BuildWebSocketUri(), token).ConfigureAwait(false);
                    await socket.SendAsync(Encoding.UTF8.GetBytes(subscribe), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                    _Socket = socket;
                    await ReceiveLoopAsync(socket, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    socket.Dispose();
                    return;
                }
                catch (Exception)
                {
                    // Connect/receive failed (hub not up yet, or the socket dropped) — fall through to retry.
                }
                finally
                {
                    _Socket = null;
                    try { socket.Dispose(); } catch (Exception) { }
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                try { await Task.Delay(_ReconnectDelay, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            StringBuilder message = new StringBuilder();

            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                string frame = message.ToString();
                message.Clear();
                Dispatch(frame);
            }
        }

        private void Dispatch(string frame)
        {
            try
            {
                FrameReceived?.Invoke(frame);
            }
            catch (Exception)
            {
                // A consumer's handler must not break the read loop.
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(frame);
                string eventType = doc.RootElement.TryGetProperty("eventType", out JsonElement type) ? (type.GetString() ?? string.Empty) : string.Empty;
                if (eventType == "run_completed")
                {
                    RunCompleted?.Invoke();
                }
                else if (eventType == "transcript_changed")
                {
                    TranscriptChanged?.Invoke();
                }
                else if (eventType == "sessions_changed")
                {
                    string sessionId = doc.RootElement.TryGetProperty("sessionId", out JsonElement sid) ? (sid.GetString() ?? string.Empty) : string.Empty;
                    SessionsChanged?.Invoke(sessionId);
                }
            }
            catch (Exception)
            {
                // Non-JSON or handler failure — ignore.
            }
        }

        private Uri BuildWebSocketUri()
        {
            string wsBase = _BaseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase)
                ? "wss" + _BaseUrl.Substring("https".Length)
                : _BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? "ws" + _BaseUrl.Substring("http".Length)
                    : _BaseUrl;
            string query = string.IsNullOrEmpty(_ApiKey) ? string.Empty : "?apiKey=" + Uri.EscapeDataString(_ApiKey!);
            return new Uri(wsBase + "/v1.0/ws" + query);
        }

        #endregion
    }
}
