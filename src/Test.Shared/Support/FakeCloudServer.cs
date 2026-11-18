namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using MuxCloud.Protocol;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;

    /// <summary>
    /// An in-process stand-in for Mux Cloud: a Watson server on a free loopback port that speaks the tunnel protocol at
    /// <c>/v1.0/tunnel</c> and answers <c>POST /v1.0/api/machines/enroll</c>. Tests script it: accept or reject the
    /// hello, answer or ignore pings, push relay frames, and close with any code.
    /// </summary>
    public sealed class FakeCloudServer : IDisposable
    {
        #region Private-Members

        private readonly Webserver _App;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly Channel<TunnelEnvelope> _Inbound = Channel.CreateUnbounded<TunnelEnvelope>();
        private readonly object _Sync = new object();
        private WebSocketSession? _Current;
        private int _Connections;

        #endregion

        #region Public-Members

        /// <summary>Loopback port.</summary>
        public int Port { get; }

        /// <summary>Server URL to put in <c>cloud.serverUrl</c>.</summary>
        public string Url => "http://127.0.0.1:" + Port;

        /// <summary>Machine id the fake accepts.</summary>
        public string MachineId { get; set; } = "mch_test0001";

        /// <summary>Machine secret the fake accepts.</summary>
        public string MachineSecret { get; set; } = TunnelProof.CreateMachineSecret();

        /// <summary>When set, the hello is answered by closing with this code instead of a welcome.</summary>
        public int? RejectHelloWith { get; set; }

        /// <summary>Whether the fake answers the machine's pings. Default true.</summary>
        public bool RespondToPings { get; set; } = true;

        /// <summary>Welcome's concurrent stream cap. Default 32.</summary>
        public int MaxConcurrentStreams { get; set; } = 32;

        /// <summary>Machine name the welcome returns, or empty to echo the hello's.</summary>
        public string WelcomeMachineName { get; set; } = string.Empty;

        /// <summary>Accepted hello payloads, in order.</summary>
        public ConcurrentQueue<TunnelHelloPayload> Hellos { get; } = new ConcurrentQueue<TunnelHelloPayload>();

        /// <summary>Tunnel connections opened so far.</summary>
        public int Connections => Volatile.Read(ref _Connections);

        /// <summary>Last enrollment request body received.</summary>
        public string LastEnrollBody { get; private set; } = string.Empty;

        /// <summary>Status code the enrollment route returns. Default 200.</summary>
        public int EnrollStatus { get; set; } = 200;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Start on a free loopback port.</summary>
        public FakeCloudServer()
        {
            Webserver? app = null;
            int port = 0;
            for (int attempt = 0; attempt < 10 && app == null; attempt++)
            {
                port = FreePort();
                WebserverSettings settings = new WebserverSettings { Hostname = "127.0.0.1", Port = port };
                settings.WebSockets.Enable = true;
                Webserver candidate = new Webserver(settings, DefaultRouteAsync);
                candidate.WebSocket(TunnelProtocol.TunnelPath, HandleTunnelAsync);
                try
                {
                    candidate.Start(_Stop.Token);
                    app = candidate;
                }
                catch (Exception)
                {
                    try { candidate.Dispose(); } catch (Exception) { }
                    Thread.Sleep(50);
                }
            }

            _App = app ?? throw new InvalidOperationException("The fake Mux Cloud server could not bind a loopback port.");
            Port = port;
        }

        #endregion

        #region Public-Methods

        /// <summary>Wait for the next frame from the machine matching a predicate (other frames are discarded).</summary>
        /// <param name="predicate">Match.</param>
        /// <param name="timeout">Timeout.</param>
        /// <returns>The frame.</returns>
        /// <exception cref="TimeoutException">No match arrived in time.</exception>
        public async Task<TunnelEnvelope> NextAsync(Func<TunnelEnvelope, bool> predicate, TimeSpan timeout)
        {
            using CancellationTokenSource cts = new CancellationTokenSource(timeout);
            try
            {
                while (true)
                {
                    TunnelEnvelope envelope = await _Inbound.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                    if (predicate(envelope)) return envelope;
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("The fake Mux Cloud did not receive the expected frame within " + timeout.TotalSeconds + " seconds.");
            }
        }

        /// <summary>Collect every frame for a stream until its end or error.</summary>
        /// <param name="streamId">Stream id.</param>
        /// <param name="timeout">Timeout.</param>
        /// <returns>Frames in arrival order, with their arrival times.</returns>
        public async Task<List<KeyValuePair<DateTime, TunnelEnvelope>>> CollectStreamAsync(string streamId, TimeSpan timeout)
        {
            List<KeyValuePair<DateTime, TunnelEnvelope>> frames = new List<KeyValuePair<DateTime, TunnelEnvelope>>();
            DateTime deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) throw new TimeoutException("Stream " + streamId + " did not finish in time.");
                TunnelEnvelope e = await NextAsync(x => x.Id == streamId, left).ConfigureAwait(false);
                frames.Add(new KeyValuePair<DateTime, TunnelEnvelope>(DateTime.UtcNow, e));
                if (e.Type == TunnelMessageTypes.HttpResponseEnd || e.Type == TunnelMessageTypes.StreamError) return frames;
            }
        }

        /// <summary>Wait until at least this many tunnel connections have been accepted with a welcome.</summary>
        /// <param name="count">Hello count.</param>
        /// <param name="timeout">Timeout.</param>
        /// <returns>True when reached.</returns>
        public async Task<bool> WaitForHellosAsync(int count, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Hellos.Count >= count) return true;
                await Task.Delay(25).ConfigureAwait(false);
            }

            return Hellos.Count >= count;
        }

        /// <summary>Send a frame to the connected machine.</summary>
        /// <param name="envelope">Frame.</param>
        /// <returns>Task.</returns>
        /// <exception cref="InvalidOperationException">No machine is connected.</exception>
        public async Task SendAsync(TunnelEnvelope envelope)
        {
            WebSocketSession session;
            lock (_Sync) { session = _Current ?? throw new InvalidOperationException("No machine is connected to the fake Mux Cloud."); }
            await session.SendTextAsync(TunnelProtocol.Serialize(envelope), _Stop.Token).ConfigureAwait(false);
        }

        /// <summary>Close the current tunnel with a code.</summary>
        /// <param name="code">Close code.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>Task.</returns>
        public async Task CloseCurrentAsync(int code, string reason)
        {
            WebSocketSession? session;
            lock (_Sync) { session = _Current; _Current = null; }
            if (session == null) return;
            try { await session.CloseAsync((WebSocketCloseStatus)code, reason, _Stop.Token).ConfigureAwait(false); } catch (Exception) { }
        }

        /// <summary>Stop the server.</summary>
        public void Dispose()
        {
            try { _Stop.Cancel(); } catch (Exception) { }
            try { _App.Stop(); } catch (Exception) { }
            try { _App.Dispose(); } catch (Exception) { }
            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task HandleTunnelAsync(HttpContextBase ctx, WebSocketSession session)
        {
            Interlocked.Increment(ref _Connections);
            bool welcomed = false;
            try
            {
                await foreach (WebSocketMessage message in session.ReadMessagesAsync(_Stop.Token).ConfigureAwait(false))
                {
                    if (message.MessageType != WebSocketMessageType.Text || string.IsNullOrEmpty(message.Text)) continue;
                    TunnelEnvelope envelope;
                    try { envelope = TunnelProtocol.Deserialize(message.Text); }
                    catch (TunnelProtocolException) { continue; }

                    if (!welcomed)
                    {
                        TunnelHelloPayload? hello = envelope.Hello;
                        bool valid = envelope.Type == TunnelMessageTypes.Hello && hello != null
                            && hello.MachineId == MachineId
                            && TunnelProof.IsTimestampFresh(hello.TimestampUtc, DateTime.UtcNow)
                            && TunnelProof.VerifyProof(MachineSecret, hello.MachineId, hello.TimestampUtc, hello.Nonce, hello.Proof);
                        int? reject = RejectHelloWith ?? (valid ? null : TunnelCloseCodes.Unauthorized);
                        if (reject.HasValue)
                        {
                            await session.CloseAsync((WebSocketCloseStatus)reject.Value, "rejected by fake", _Stop.Token).ConfigureAwait(false);
                            return;
                        }

                        Hellos.Enqueue(hello!);
                        lock (_Sync) { _Current = session; }
                        welcomed = true;
                        TunnelWelcomePayload welcome = new TunnelWelcomePayload
                        {
                            ConnectionId = "mcn_" + Guid.NewGuid().ToString("N").Substring(0, 12),
                            ServerVersion = "0.0.0-fake",
                            HeartbeatSeconds = 20,
                            MaxConcurrentStreams = MaxConcurrentStreams,
                            Capabilities = TunnelProtocol.IntersectCapabilities(TunnelCapabilities.All, hello!.Capabilities),
                            MachineName = string.IsNullOrEmpty(WelcomeMachineName) ? hello.MachineName : WelcomeMachineName
                        };
                        await session.SendTextAsync(TunnelProtocol.Serialize(TunnelProtocol.CreateWelcome(welcome)), _Stop.Token).ConfigureAwait(false);
                        continue;
                    }

                    if (envelope.Type == TunnelMessageTypes.Ping)
                    {
                        if (RespondToPings) await session.SendTextAsync(TunnelProtocol.Serialize(TunnelProtocol.CreatePong(envelope)), _Stop.Token).ConfigureAwait(false);
                        continue;
                    }

                    _Inbound.Writer.TryWrite(envelope);
                }
            }
            catch (Exception)
            {
                // Connection ended.
            }
            finally
            {
                lock (_Sync) { if (ReferenceEquals(_Current, session)) _Current = null; }
            }
        }

        private async Task DefaultRouteAsync(HttpContextBase ctx)
        {
            if (ctx.Request.Method == WatsonWebserver.Core.HttpMethod.POST && (ctx.Request.Url.RawWithoutQuery ?? string.Empty) == TunnelProtocol.EnrollPath)
            {
                LastEnrollBody = ctx.Request.DataAsString ?? string.Empty;
                ctx.Response.StatusCode = EnrollStatus;
                ctx.Response.ContentType = "application/json";
                if (EnrollStatus == 200)
                {
                    await ctx.Response.Send("{\"machineId\":\"" + MachineId + "\",\"machineSecret\":\"" + MachineSecret + "\",\"machineName\":\"fake-name\",\"accountEmail\":\"dev@example.test\"}").ConfigureAwait(false);
                }
                else
                {
                    await ctx.Response.Send("{\"error\":\"Unauthorized\",\"message\":\"bad credentials\"}").ConfigureAwait(false);
                }

                return;
            }

            ctx.Response.StatusCode = 404;
            await ctx.Response.Send("{}").ConfigureAwait(false);
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
