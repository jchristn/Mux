namespace Test.Shared
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Mcp;

    /// <summary>
    /// A simple test fixture that hosts an MCP server over streamable HTTP for integration testing.
    /// </summary>
    public class TestMcpHttpServer : IDisposable
    {
        #region Private-Members

        private readonly McpHttpServer _Server;
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private Task? _RunTask = null;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The base URL for the HTTP MCP server.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// The MCP path for streamable HTTP requests.
        /// </summary>
        public string McpPath => "/mcp";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="TestMcpHttpServer"/> class.
        /// </summary>
        public TestMcpHttpServer()
        {
            int port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            _Server = new McpHttpServer("127.0.0.1", port);
            _Server.RegisterTool(
                "echo",
                "Returns the input text",
                new
                {
                    type = "object",
                    properties = new
                    {
                        text = new { type = "string" }
                    }
                },
                args =>
                {
                    string text = args != null && args.ContainsProperty("text")
                        ? args.GetString("text") ?? string.Empty
                        : string.Empty;

                    return (object)new
                    {
                        content = new object[]
                        {
                            new { type = "text", text = text }
                        }
                    };
                });
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the HTTP MCP server.
        /// </summary>
        /// <returns>A task that completes once the listener has been started.</returns>
        public async Task StartAsync()
        {
            _RunTask = Task.Run(() => _Server.StartAsync(_Cts.Token));
            await WaitForHealthAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Stops the HTTP MCP server.
        /// </summary>
        public void Stop()
        {
            if (_Disposed)
            {
                return;
            }

            _Cts.Cancel();
            _Server.Stop();
            try
            {
                _RunTask?.Wait(5000);
            }
            catch (AggregateException)
            {
                // Ignore cancellation/stop exceptions during teardown.
            }
        }

        /// <summary>
        /// Releases all resources used by this <see cref="TestMcpHttpServer"/> instance.
        /// </summary>
        public void Dispose()
        {
            if (!_Disposed)
            {
                Stop();
                _Server.Dispose();
                _Cts.Dispose();
                _Disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task WaitForHealthAsync()
        {
            // Short per-request timeout so a single stalled connect cannot eat the whole budget, and a
            // generous overall budget so a slow or heavily loaded CI runner (where process/port setup can
            // lag by many seconds) does not fail spuriously. Any HTTP response — even 404/405 — means the
            // listener is accepting connections, which is all "started" requires here.
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                // If the server task already failed (e.g. the port was taken between GetFreePort and bind),
                // surface its real error immediately instead of waiting out the full budget on a vague timeout.
                if (_RunTask != null && _RunTask.IsFaulted)
                {
                    throw new InvalidOperationException(
                        "The HTTP MCP test server failed to start at " + BaseUrl + ".",
                        _RunTask.Exception?.GetBaseException());
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(BaseUrl, _Cts.Token).ConfigureAwait(false);
                    return;
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }

                await Task.Delay(100, _Cts.Token).ConfigureAwait(false);
            }

            string detail = _RunTask != null && _RunTask.IsFaulted
                ? " Server task error: " + (_RunTask.Exception?.GetBaseException().Message ?? "unknown")
                : string.Empty;
            throw new InvalidOperationException(
                "Timed out after 30s waiting for the HTTP MCP test server to start at " + BaseUrl + "." + detail);
        }

        private static int GetFreePort()
        {
            using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
