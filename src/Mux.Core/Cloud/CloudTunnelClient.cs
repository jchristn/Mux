namespace Mux.Core.Cloud
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.IO;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MuxCloud.Protocol;

    /// <summary>
    /// Keeps this machine's one persistent outbound WebSocket to Mux Cloud. It sends <c>tunnel.hello</c> with a proof of
    /// the machine secret, counts the machine online after <c>tunnel.welcome</c>, pings on the agreed interval and
    /// reconnects after three missed pongs, reconnects with exponential backoff and jitter, and stops on terminal close
    /// codes. The receive loop only parses and dispatches: every relayed request and channel runs on its own task
    /// (bounded by the negotiated maximum), so one slow request never blocks another or the heartbeat.
    /// <para>Thread safety: <see cref="Start"/>, <see cref="StopAsync"/>, <see cref="Status"/>, and
    /// <see cref="SendMachineStatusAsync"/> are safe to call from any thread.</para>
    /// </summary>
    public sealed class CloudTunnelClient : IDisposable
    {
        #region Private-Members

        private const int _MaxFrameBytes = 16 * 1024 * 1024;

        private readonly CloudTunnelOptions _Options;
        private readonly CloudRelayForwarder _Forwarder;
        private readonly object _Sync = new object();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _Streams = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CloudWsChannel> _Channels = new ConcurrentDictionary<string, CloudWsChannel>(StringComparer.Ordinal);
        private CloudStatus _Status = new CloudStatus();
        private CancellationTokenSource? _Stop;
        private Task? _Loop;
        private WebSocketTunnelSender? _Sender;
        private int _ActiveStreams;
        private int _MaxStreams;
        private int _MissedPongs;
        private string _LastPingNonce = string.Empty;
        private long _LastPingTicks;
        private TimeSpan? _RetryAfter;
        private bool _Disposed;

        #endregion

        #region Public-Events

        /// <summary>Raised with a snapshot whenever the status changes. Handlers must not throw and must not block.</summary>
        public event Action<CloudStatus>? StatusChanged;

        #endregion

        #region Public-Members

        /// <summary>A snapshot of the current status.</summary>
        public CloudStatus Status
        {
            get { lock (_Sync) { return _Status.Clone(); } }
        }

        /// <summary>Number of relayed streams and channels in flight.</summary>
        public int ActiveStreams => Volatile.Read(ref _ActiveStreams);

        /// <summary>Whether the tunnel is currently connected (welcomed).</summary>
        public bool IsConnected
        {
            get { lock (_Sync) { return _Status.State == CloudStateEnum.Connected; } }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="options">Connection settings.</param>
        /// <param name="forwarder">Forwarder to the local hub.</param>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        /// <exception cref="ArgumentException">The machine credential is missing.</exception>
        public CloudTunnelClient(CloudTunnelOptions options, CloudRelayForwarder forwarder)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _Forwarder = forwarder ?? throw new ArgumentNullException(nameof(forwarder));
            if (string.IsNullOrWhiteSpace(options.MachineId) || string.IsNullOrWhiteSpace(options.MachineSecret))
            {
                throw new ArgumentException("The machine is not enrolled in Mux Cloud (no machine id or secret). Run mux cloud login.", nameof(options));
            }

            _MaxStreams = options.MaxConcurrentStreams;
            _Status = new CloudStatus
            {
                State = CloudStateEnum.Disabled,
                ServerUrl = options.ServerUrl,
                MachineId = options.MachineId,
                MachineName = options.MachineName,
                AccountEmail = options.AccountEmail,
                AllowRemoteToolApproval = options.AllowRemoteToolApproval,
                HostedHere = true
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the connection loop. Idempotent.</summary>
        /// <exception cref="ObjectDisposedException">The client was disposed.</exception>
        public void Start()
        {
            if (_Disposed) throw new ObjectDisposedException(nameof(CloudTunnelClient));
            lock (_Sync)
            {
                if (_Loop != null) return;
                _Stop = new CancellationTokenSource();
                CancellationToken token = _Stop.Token;
                _Loop = Task.Run(() => RunAsync(token));
            }
        }

        /// <summary>Stop the connection loop and close the tunnel.</summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            Task? loop;
            lock (_Sync)
            {
                loop = _Loop;
                _Loop = null;
                try { _Stop?.Cancel(); } catch (ObjectDisposedException) { }
            }

            if (loop != null)
            {
                try { await loop.ConfigureAwait(false); } catch (Exception) { }
            }

            lock (_Sync)
            {
                _Stop?.Dispose();
                _Stop = null;
            }

            UpdateStatus(s => { s.State = CloudStateEnum.Disabled; s.Reason = string.Empty; s.ReasonCode = CloudReasonCodes.TurnedOff; s.ConnectionId = string.Empty; s.ConnectedUtc = null; s.RetryInSeconds = null; });
        }

        /// <summary>Send a <c>machine.status</c> push when connected.</summary>
        /// <param name="status">Status payload.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when sent.</returns>
        public async Task<bool> SendMachineStatusAsync(MachineStatusPayload status, CancellationToken token)
        {
            if (status == null) return false;
            WebSocketTunnelSender? sender;
            lock (_Sync) { sender = _Status.State == CloudStateEnum.Connected ? _Sender : null; }
            if (sender == null) return false;
            return await sender.SendAsync(TunnelProtocol.CreateMachineStatus(status), token).ConfigureAwait(false);
        }

        /// <summary>Stop and release resources.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            try { StopAsync().GetAwaiter().GetResult(); } catch (Exception) { }
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken stop)
        {
            TunnelReconnectPolicy policy = new TunnelReconnectPolicy { BaseDelaySeconds = _Options.ReconnectBaseDelaySeconds, MaxDelaySeconds = _Options.ReconnectMaxDelaySeconds, Jitter = 0.2 };
            int attempt = 0;
            bool connectedBefore = false;
            string reason = string.Empty;
            string reasonCode = string.Empty;
            while (!stop.IsCancellationRequested)
            {
                bool retrying = connectedBefore || attempt > 0;
                string shownReason = reason;
                string shownCode = reasonCode;
                UpdateStatus(s => { s.State = retrying ? CloudStateEnum.Reconnecting : CloudStateEnum.Connecting; s.Reason = shownReason; s.ReasonCode = shownCode; s.RetryInSeconds = null; });

                CloudConnectionOutcome outcome = await RunConnectionAsync(stop).ConfigureAwait(false);
                if (stop.IsCancellationRequested) break;
                if (outcome.Terminal)
                {
                    UpdateStatus(s => { s.State = outcome.TerminalState; s.Reason = outcome.Reason; s.ReasonCode = outcome.ReasonCode; s.ConnectionId = string.Empty; s.ConnectedUtc = null; s.RetryInSeconds = null; });
                    return;
                }

                if (outcome.Welcomed)
                {
                    connectedBefore = true;
                    attempt = 0;
                }

                attempt++;
                reason = outcome.Reason;
                reasonCode = outcome.ReasonCode;
                TimeSpan delay = outcome.RetryAfter ?? policy.ComputeDelay(attempt, Random.Shared);
                if (outcome.CloseCode == TunnelCloseCodes.Replaced) delay = TimeSpan.FromSeconds(policy.MaxDelaySeconds);
                double seconds = delay.TotalSeconds;
                string retryReason = reason;
                string retryCode = reasonCode;
                UpdateStatus(s => { s.State = CloudStateEnum.Reconnecting; s.Reason = retryReason; s.ReasonCode = retryCode; s.ConnectionId = string.Empty; s.ConnectedUtc = null; s.RetryInSeconds = Math.Round(seconds, 1); });
                try
                {
                    await Task.Delay(delay, stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task<CloudConnectionOutcome> RunConnectionAsync(CancellationToken stop)
        {
            CloudConnectionOutcome outcome = new CloudConnectionOutcome();
            _RetryAfter = null;
            Uri uri;
            try
            {
                uri = TunnelProtocol.ToTunnelUri(_Options.ServerUrl);
            }
            catch (ArgumentException e)
            {
                outcome.Terminal = true;
                outcome.TerminalState = CloudStateEnum.Disabled;
                outcome.Reason = e.Message;
                return outcome;
            }

            using ClientWebSocket socket = new ClientWebSocket();
            if (_Options.AllowInvalidCertificates) socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
            try
            {
                using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(_Options.HandshakeTimeoutSeconds));
                await socket.ConnectAsync(uri, connectTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is IOException || e is InvalidOperationException)
            {
                outcome.Reason = stop.IsCancellationRequested ? string.Empty : "Could not reach " + uri.GetLeftPart(UriPartial.Authority) + ": " + Root(e).Message;
                outcome.ReasonCode = stop.IsCancellationRequested ? string.Empty : CloudReasonCodes.Unreachable;
                return outcome;
            }

            using WebSocketTunnelSender sender = new WebSocketTunnelSender(socket);
            string timestamp = TunnelProof.FormatTimestamp(DateTime.UtcNow);
            string nonce = TunnelProof.CreateNonce();
            TunnelHelloPayload hello = new TunnelHelloPayload
            {
                MachineId = _Options.MachineId,
                ProtocolVersion = TunnelProtocol.CurrentVersion,
                MuxVersion = _Options.MuxVersion,
                MachineName = _Options.MachineName,
                Hostname = CloudMachineInfo.Hostname(),
                Os = CloudMachineInfo.Os(),
                Capabilities = new System.Collections.Generic.List<string>(TunnelCapabilities.All),
                TimestampUtc = timestamp,
                Nonce = nonce,
                Proof = TunnelProof.ComputeProof(_Options.MachineSecret, _Options.MachineId, timestamp, nonce)
            };

            if (!await sender.SendAsync(TunnelProtocol.CreateHello(hello), stop).ConfigureAwait(false))
            {
                outcome.Reason = "The tunnel closed before the handshake was sent.";
                outcome.ReasonCode = CloudReasonCodes.ConnectionDropped;
                return outcome;
            }

            TunnelEnvelope? first;
            try
            {
                using CancellationTokenSource handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(_Options.HandshakeTimeoutSeconds));
                first = await ReceiveEnvelopeAsync(socket, handshakeTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is TunnelProtocolException || e is IOException)
            {
                first = null;
                if (!stop.IsCancellationRequested && socket.CloseStatus == null)
                {
                    outcome.Reason = "No handshake reply from Mux Cloud: " + Root(e).Message;
                    outcome.ReasonCode = CloudReasonCodes.HandshakeRejected;
                }
            }

            if (first == null || first.Type != TunnelMessageTypes.Welcome || first.Welcome == null)
            {
                ApplyClose(socket, outcome, false);
                if (string.IsNullOrEmpty(outcome.Reason))
                {
                    outcome.Reason = "Mux Cloud did not accept the handshake.";
                    outcome.ReasonCode = CloudReasonCodes.HandshakeRejected;
                }
                await CloseQuietlyAsync(socket).ConfigureAwait(false);
                return outcome;
            }

            TunnelWelcomePayload welcome = first.Welcome;
            outcome.Welcomed = true;
            _MaxStreams = Math.Max(1, welcome.MaxConcurrentStreams);
            Interlocked.Exchange(ref _MissedPongs, 0);
            lock (_Sync) { _Sender = sender; }
            UpdateStatus(s =>
            {
                s.State = CloudStateEnum.Connected;
                s.Reason = string.Empty;
                s.ReasonCode = string.Empty;
                s.ConnectionId = welcome.ConnectionId;
                s.ConnectedUtc = DateTime.UtcNow;
                s.RetryInSeconds = null;
                if (!string.IsNullOrWhiteSpace(welcome.MachineName)) s.MachineName = welcome.MachineName;
            });

            TimeSpan heartbeat = _Options.HeartbeatIntervalOverride ?? TimeSpan.FromSeconds(welcome.HeartbeatSeconds);
            using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(stop);
            Task heartbeatLoop = HeartbeatLoopAsync(socket, sender, heartbeat, outcome, connection.Token);
            try
            {
                await ReceiveLoopAsync(socket, sender, outcome, connection.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_Sync) { _Sender = null; }
                connection.Cancel();
                foreach (CancellationTokenSource stream in _Streams.Values)
                {
                    try { stream.Cancel(); } catch (ObjectDisposedException) { }
                }

                foreach (CloudWsChannel channel in _Channels.Values)
                {
                    channel.Close(1001, "tunnel closed");
                }

                try { await heartbeatLoop.ConfigureAwait(false); } catch (Exception) { }
            }

            ApplyClose(socket, outcome, true);
            await CloseQuietlyAsync(socket).ConfigureAwait(false);
            if (_RetryAfter.HasValue && !outcome.Terminal) outcome.RetryAfter = _RetryAfter;
            return outcome;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, WebSocketTunnelSender sender, CloudConnectionOutcome outcome, CancellationToken token)
        {
            while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                TunnelEnvelope? envelope;
                try
                {
                    envelope = await ReceiveEnvelopeAsync(socket, token).ConfigureAwait(false);
                }
                catch (TunnelProtocolException)
                {
                    continue;
                }
                catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is IOException)
                {
                    if (string.IsNullOrEmpty(outcome.Reason) && !token.IsCancellationRequested)
                    {
                        outcome.Reason = "The connection to Mux Cloud dropped: " + Root(e).Message;
                        outcome.ReasonCode = CloudReasonCodes.ConnectionDropped;
                    }
                    return;
                }

                if (envelope == null) return;
                await DispatchAsync(envelope, sender, token).ConfigureAwait(false);
            }
        }

        private async Task DispatchAsync(TunnelEnvelope envelope, WebSocketTunnelSender sender, CancellationToken token)
        {
            string id = envelope.Id ?? string.Empty;
            switch (envelope.Type)
            {
                case TunnelMessageTypes.Ping:
                    await sender.SendAsync(TunnelProtocol.CreatePong(envelope), token).ConfigureAwait(false);
                    break;

                case TunnelMessageTypes.Pong:
                    string? nonce = envelope.Ping?.Nonce;
                    if (!string.IsNullOrEmpty(nonce) && string.Equals(nonce, Volatile.Read(ref _LastPingNonce), StringComparison.Ordinal))
                    {
                        Interlocked.Exchange(ref _MissedPongs, 0);
                        double ms = Stopwatch.GetElapsedTime(Interlocked.Read(ref _LastPingTicks)).TotalMilliseconds;
                        UpdateStatus(s => { s.LatencyMs = Math.Round(ms, 1); }, false);
                    }

                    break;

                case TunnelMessageTypes.GoAway:
                    _RetryAfter = TimeSpan.FromSeconds(envelope.GoAway?.RetryAfterSeconds ?? 5);
                    break;

                case TunnelMessageTypes.HttpRequest:
                    if (envelope.HttpRequest == null || id.Length == 0) break;
                    if (!TryReserveStream())
                    {
                        await sender.SendAsync(TunnelProtocol.CreateStreamError(id, TunnelErrorCodes.TooManyStreams, "This machine is already serving its maximum number of concurrent relayed requests.", 503), token).ConfigureAwait(false);
                        break;
                    }

                    CancellationTokenSource streamCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    _Streams[id] = streamCts;
                    HttpRequestPayload request = envelope.HttpRequest;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _Forwarder.HandleHttpAsync(id, request, sender, streamCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // The forwarder reports its own failures; never let one stream fault the tunnel.
                        }
                        finally
                        {
                            _Streams.TryRemove(id, out _);
                            streamCts.Dispose();
                            Interlocked.Decrement(ref _ActiveStreams);
                        }
                    }, CancellationToken.None);
                    break;

                case TunnelMessageTypes.HttpCancel:
                    if (_Streams.TryGetValue(id, out CancellationTokenSource? cancel))
                    {
                        try { cancel.Cancel(); } catch (ObjectDisposedException) { }
                    }

                    break;

                case TunnelMessageTypes.WsOpen:
                    if (envelope.WsOpen == null || id.Length == 0) break;
                    if (!TryReserveStream())
                    {
                        await sender.SendAsync(TunnelProtocol.CreateStreamError(id, TunnelErrorCodes.TooManyStreams, "This machine is already serving its maximum number of concurrent relayed connections.", 503), token).ConfigureAwait(false);
                        break;
                    }

                    CloudWsChannel channel = new CloudWsChannel(id);
                    _Channels[id] = channel;
                    WsOpenPayload open = envelope.WsOpen;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _Forwarder.HandleWebSocketAsync(channel, open, sender, token).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // Reported by the forwarder.
                        }
                        finally
                        {
                            _Channels.TryRemove(id, out _);
                            Interlocked.Decrement(ref _ActiveStreams);
                        }
                    }, CancellationToken.None);
                    break;

                case TunnelMessageTypes.WsFrame:
                    if (_Channels.TryGetValue(id, out CloudWsChannel? target) && envelope.WsFrame != null) target.Enqueue(envelope.WsFrame.Text);
                    break;

                case TunnelMessageTypes.WsClose:
                    if (_Channels.TryGetValue(id, out CloudWsChannel? closing)) closing.Close(envelope.WsClose?.Code ?? 1000, envelope.WsClose?.Reason ?? string.Empty);
                    break;

                default:
                    // Unknown or machine-to-cloud types: ignore for forward compatibility.
                    break;
            }
        }

        private bool TryReserveStream()
        {
            while (true)
            {
                int current = Volatile.Read(ref _ActiveStreams);
                if (current >= _MaxStreams) return false;
                if (Interlocked.CompareExchange(ref _ActiveStreams, current + 1, current) == current) return true;
            }
        }

        private async Task HeartbeatLoopAsync(ClientWebSocket socket, WebSocketTunnelSender sender, TimeSpan interval, CloudConnectionOutcome outcome, CancellationToken token)
        {
            if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(20);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (Volatile.Read(ref _MissedPongs) >= _Options.MissedPongLimit)
                {
                    outcome.Reason = "Mux Cloud stopped answering heartbeats.";
                    outcome.ReasonCode = CloudReasonCodes.HeartbeatTimeout;
                    outcome.CloseCode = TunnelCloseCodes.HeartbeatTimeout;
                    try { socket.Abort(); } catch (Exception) { }
                    return;
                }

                TunnelEnvelope ping = TunnelProtocol.CreatePing();
                Volatile.Write(ref _LastPingNonce, ping.Ping!.Nonce);
                Interlocked.Exchange(ref _LastPingTicks, Stopwatch.GetTimestamp());
                Interlocked.Increment(ref _MissedPongs);
                await sender.SendAsync(ping, token).ConfigureAwait(false);
            }
        }

        private static async Task<TunnelEnvelope?> ReceiveEnvelopeAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[32 * 1024];
            using MemoryStream message = new MemoryStream();
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                if (result.Count > 0) message.Write(buffer, 0, result.Count);
                if (message.Length > _MaxFrameBytes) throw new TunnelProtocolException("A tunnel frame exceeded " + _MaxFrameBytes + " bytes.");
                if (!result.EndOfMessage) continue;
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    message.SetLength(0);
                    continue;
                }

                return TunnelProtocol.Deserialize(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            }
        }

        private static void ApplyClose(ClientWebSocket socket, CloudConnectionOutcome outcome, bool welcomed)
        {
            if (socket.CloseStatus == null) return;
            int code = (int)socket.CloseStatus.Value;
            string description = socket.CloseStatusDescription ?? string.Empty;
            outcome.CloseCode = code;
            switch (code)
            {
                case TunnelCloseCodes.Unauthorized:
                    outcome.Terminal = true;
                    outcome.TerminalState = CloudStateEnum.SignedOut;
                    outcome.Reason = "Mux Cloud rejected this machine's credential" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.CredentialRejected;
                    break;
                case TunnelCloseCodes.Revoked:
                    outcome.Terminal = true;
                    outcome.TerminalState = CloudStateEnum.Revoked;
                    outcome.Reason = "This machine was removed from Mux Cloud" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.Removed;
                    break;
                case TunnelCloseCodes.UnsupportedVersion:
                    outcome.Terminal = true;
                    outcome.TerminalState = CloudStateEnum.Disabled;
                    outcome.Reason = "Mux Cloud requires a newer mux. Upgrade mux, then reconnect" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.UpgradeRequired;
                    break;
                case TunnelCloseCodes.Replaced:
                    outcome.Reason = "Another connection for this machine replaced this one" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.Replaced;
                    break;
                case TunnelCloseCodes.Draining:
                    outcome.Reason = "Mux Cloud is restarting" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.ServerRestarting;
                    break;
                case TunnelCloseCodes.RateLimited:
                    outcome.Reason = "Too many failed connection attempts; waiting before retrying" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.RateLimited;
                    break;
                case TunnelCloseCodes.HeartbeatTimeout:
                    outcome.Reason = "Mux Cloud timed out waiting for this machine's heartbeat" + Suffix(description);
                    outcome.ReasonCode = CloudReasonCodes.HeartbeatTimeout;
                    break;
                case TunnelCloseCodes.BadHandshake:
                    outcome.Reason = "Mux Cloud rejected the handshake" + Suffix(description);
                    outcome.ReasonCode = IsClockSkew(description) ? CloudReasonCodes.ClockSkew : CloudReasonCodes.HandshakeRejected;
                    break;
                default:
                    if (string.IsNullOrEmpty(outcome.Reason)) outcome.Reason = welcomed ? "Mux Cloud closed the connection" + Suffix(description) : "Mux Cloud closed the connection during the handshake" + Suffix(description);
                    if (string.IsNullOrEmpty(outcome.ReasonCode)) outcome.ReasonCode = welcomed ? CloudReasonCodes.ConnectionDropped : CloudReasonCodes.HandshakeRejected;
                    break;
            }
        }

        private static bool IsClockSkew(string description)
        {
            // MuxCloud closes a handshake whose timestamp is outside the allowed window with 4400 and a reason that names
            // the clock ("... from server time; check this machine's clock").
            return description.IndexOf("clock", StringComparison.OrdinalIgnoreCase) >= 0 || description.IndexOf("server time", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Suffix(string description)
        {
            return string.IsNullOrWhiteSpace(description) ? "." : ": " + description;
        }

        private static async Task CloseQuietlyAsync(ClientWebSocket socket)
        {
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closing", timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Best effort.
            }
        }

        private static Exception Root(Exception e)
        {
            Exception current = e;
            while (current.InnerException != null) current = current.InnerException;
            return current;
        }

        private void UpdateStatus(Action<CloudStatus> change, bool notify = true)
        {
            CloudStatus snapshot;
            lock (_Sync)
            {
                change(_Status);
                snapshot = _Status.Clone();
            }

            if (!notify) return;
            try { StatusChanged?.Invoke(snapshot); }
            catch (Exception) { /* a listener must never break the tunnel */ }
        }

        #endregion
    }
}
