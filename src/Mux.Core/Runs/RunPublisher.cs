namespace Mux.Core.Runs
{
    using System;
    using System.Collections.Generic;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;

    /// <summary>
    /// Publishes a run's events to a mux hub server over the WebSocket bridge so other surfaces can mirror it
    /// live. Used by surfaces that drive the engine in their own process (the desktop app, the terminal) to
    /// feed their in-process runs into the shared hub's registry — the reverse direction of a mirror
    /// subscription. Each event is serialized with <see cref="AgentEventSerializer"/> (the canonical envelope)
    /// and sent as a <c>publish</c> frame; the hub relays it verbatim to subscribers.
    ///
    /// <para>Publishing is best-effort: a connect or send failure is swallowed so it never disrupts the run
    /// the caller is actually executing. Call <see cref="StartAsync"/> once, <see cref="PublishAsync"/> per
    /// event, then dispose. Not thread-safe for concurrent <see cref="PublishAsync"/> calls from multiple
    /// threads — a single run loop drives it sequentially.</para>
    /// </summary>
    public sealed class RunPublisher : IAsyncDisposable
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false
        };

        private readonly string _BaseUrl;
        private readonly string? _ApiKey;
        private readonly SemaphoreSlim _SendLock = new SemaphoreSlim(1, 1);

        private ClientWebSocket? _Socket;
        private string _RunId = string.Empty;
        private string _SessionId = string.Empty;
        private string _EndpointName = string.Empty;
        private string _Model = string.Empty;
        private bool _Connected;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a publisher targeting a hub server.
        /// </summary>
        /// <param name="baseUrl">The hub base URL, for example <c>http://127.0.0.1:8710</c>.</param>
        /// <param name="apiKey">The hub API key, or null when it runs without one.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is null or empty.</exception>
        public RunPublisher(string baseUrl, string? apiKey)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _ApiKey = apiKey;
        }

        #endregion

        #region Public-Members

        /// <summary>Whether the publisher is connected to the hub (false when the connection failed).</summary>
        public bool IsConnected
        {
            get => _Connected;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Connects to the hub and prepares to publish a run. Best-effort: on failure the publisher stays
        /// disconnected and subsequent <see cref="PublishAsync"/> calls are no-ops.
        /// </summary>
        /// <param name="runId">The run correlation id. Must not be null or empty.</param>
        /// <param name="sessionId">The session the run belongs to.</param>
        /// <param name="endpointName">The endpoint the run targets.</param>
        /// <param name="model">The model the run targets.</param>
        /// <param name="cancellationToken">A token to cancel the connect.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="runId"/> is null or empty.</exception>
        public async Task StartAsync(string runId, string sessionId, string endpointName, string model, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(runId)) throw new ArgumentException("Run id is required.", nameof(runId));

            _RunId = runId;
            _SessionId = sessionId ?? string.Empty;
            _EndpointName = endpointName ?? string.Empty;
            _Model = model ?? string.Empty;

            try
            {
                ClientWebSocket socket = new ClientWebSocket();
                await socket.ConnectAsync(BuildWebSocketUri(), cancellationToken).ConfigureAwait(false);
                _Socket = socket;
                _Connected = true;
            }
            catch (Exception)
            {
                _Connected = false;
            }
        }

        /// <summary>
        /// Publishes one run event to the hub. No-op when not connected. Best-effort — a send failure is
        /// swallowed.
        /// </summary>
        /// <param name="agentEvent">The event to publish. Null is ignored.</param>
        /// <param name="cancellationToken">A token to cancel the send.</param>
        public async Task PublishAsync(AgentEvent? agentEvent, CancellationToken cancellationToken)
        {
            if (agentEvent == null || !_Connected || _Socket == null)
            {
                return;
            }

            // Publishing is strictly best-effort — it must NEVER throw, because callers publish from inside the
            // turn's projection loop; an exception here (a serialization edge case, a lock/send fault) would
            // abort the loop and leave the turn unfinished (no persist, a stuck in-flight flag). Swallow every
            // failure and mark the connection dead so later events simply no-op.
            byte[] payload;
            try
            {
                string envelope = AgentEventSerializer.ToEnvelopeLine(agentEvent);
                Dictionary<string, object?> publishFrame = new Dictionary<string, object?>
                {
                    ["action"] = "publish",
                    ["runId"] = _RunId,
                    ["sessionId"] = _SessionId,
                    ["endpointName"] = _EndpointName,
                    ["model"] = _Model,
                    ["frame"] = envelope
                };
                payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(publishFrame, _JsonOptions));
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                await _SendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                await _Socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                _Connected = false;
            }
            finally
            {
                _SendLock.Release();
            }
        }

        /// <summary>
        /// Closes the connection to the hub. Safe to call more than once.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            _Connected = false;
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

            _SendLock.Dispose();
        }

        #endregion

        #region Private-Methods

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
