namespace Test.Shared.Support
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Net.WebSockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.WebSockets;

    /// <summary>
    /// A scripted stand-in for the local mux hub that the tunnel forwards to: fast and slow routes, a server-sent event
    /// stream with gaps, an endless stream for cancellation, a header echo, a body echo, and a WebSocket echo.
    /// </summary>
    public sealed class LocalHubStub : IDisposable
    {
        #region Private-Members

        private readonly Webserver _App;
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private int _WsCloses;

        #endregion

        #region Public-Members

        /// <summary>Loopback port.</summary>
        public int Port { get; }

        /// <summary>Base URL.</summary>
        public string BaseUrl => "http://127.0.0.1:" + Port;

        /// <summary>How long <c>/slow</c> waits before answering. Default 3 seconds.</summary>
        public TimeSpan SlowDelay { get; set; } = TimeSpan.FromSeconds(3);

        /// <summary>Number of local WebSocket sessions that ended.</summary>
        public int WebSocketCloses => Volatile.Read(ref _WsCloses);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Start on a free loopback port.</summary>
        public LocalHubStub()
        {
            Webserver? app = null;
            int port = 0;
            for (int attempt = 0; attempt < 10 && app == null; attempt++)
            {
                port = FreePort();
                WebserverSettings settings = new WebserverSettings { Hostname = "127.0.0.1", Port = port };
                settings.WebSockets.Enable = true;
                Webserver candidate = new Webserver(settings, RouteAsync);
                candidate.WebSocket("/echo", EchoAsync);
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

            _App = app ?? throw new InvalidOperationException("The local hub stub could not bind a loopback port.");
            Port = port;
        }

        #endregion

        #region Public-Methods

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

        private async Task RouteAsync(HttpContextBase ctx)
        {
            string path = ctx.Request.Url.RawWithoutQuery ?? string.Empty;
            switch (path)
            {
                case "/fast":
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/plain";
                    await ctx.Response.Send("fast").ConfigureAwait(false);
                    return;

                case "/slow":
                    await Task.Delay(SlowDelay, _Stop.Token).ConfigureAwait(false);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/plain";
                    await ctx.Response.Send("slow").ConfigureAwait(false);
                    return;

                case "/sse":
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/event-stream";
                    ctx.Response.ServerSentEvents = true;
                    await ctx.Response.SendEvent(new ServerSentEvent { Event = "token", Data = "one" }, false, _Stop.Token).ConfigureAwait(false);
                    await Task.Delay(700, _Stop.Token).ConfigureAwait(false);
                    await ctx.Response.SendEvent(new ServerSentEvent { Event = "token", Data = "two" }, false, _Stop.Token).ConfigureAwait(false);
                    await Task.Delay(100, _Stop.Token).ConfigureAwait(false);
                    await ctx.Response.SendEvent(new ServerSentEvent { Event = "done", Data = "end" }, true, _Stop.Token).ConfigureAwait(false);
                    return;

                case "/forever":
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/event-stream";
                    ctx.Response.ServerSentEvents = true;
                    try
                    {
                        for (int i = 0; i < 200; i++)
                        {
                            await ctx.Response.SendEvent(new ServerSentEvent { Event = "tick", Data = i.ToString(System.Globalization.CultureInfo.InvariantCulture) }, false, _Stop.Token).ConfigureAwait(false);
                            await Task.Delay(100, _Stop.Token).ConfigureAwait(false);
                        }

                        await ctx.Response.SendEvent(new ServerSentEvent { Event = "done", Data = "end" }, true, _Stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // The relay aborted the request.
                    }

                    return;

                case "/headers":
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.Send(JsonSerializer.Serialize(new
                    {
                        authorization = ctx.Request.Headers["Authorization"] ?? string.Empty,
                        relay = ctx.Request.Headers["X-Mux-Relay"] ?? string.Empty,
                        basePath = ctx.Request.Headers["X-Mux-Base-Path"] ?? string.Empty,
                        cookie = ctx.Request.Headers["Cookie"] ?? string.Empty,
                        forwardedFor = ctx.Request.Headers["X-Forwarded-For"] ?? string.Empty,
                        accept = ctx.Request.Headers["Accept"] ?? string.Empty,
                        query = ctx.Request.Url.Full ?? string.Empty
                    })).ConfigureAwait(false);
                    return;

                case "/echo-body":
                    ctx.Response.StatusCode = 201;
                    ctx.Response.ContentType = ctx.Request.ContentType ?? "application/octet-stream";
                    await ctx.Response.Send(ctx.Request.DataAsString ?? string.Empty).ConfigureAwait(false);
                    return;

                default:
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.Send("missing").ConfigureAwait(false);
                    return;
            }
        }

        private async Task EchoAsync(HttpContextBase ctx, WebSocketSession session)
        {
            try
            {
                await foreach (WebSocketMessage message in session.ReadMessagesAsync(_Stop.Token).ConfigureAwait(false))
                {
                    if (message.MessageType != WebSocketMessageType.Text) continue;
                    string auth = ctx.Request.Headers["Authorization"] ?? string.Empty;
                    await session.SendTextAsync("echo:" + message.Text + "|" + auth, _Stop.Token).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                Interlocked.Increment(ref _WsCloses);
            }
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
