namespace Test.Shared
{
    using System;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic;

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
                    string text = args?.TryGetProperty("text", out JsonElement textElement) == true
                        ? textElement.GetString() ?? string.Empty
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
            using HttpClient client = new HttpClient();

            for (int i = 0; i < 50; i++)
            {
                try
                {
                    using HttpResponseMessage response = await client.GetAsync(BaseUrl, _Cts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }

                await Task.Delay(100, _Cts.Token).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Timed out waiting for the HTTP MCP test server to start.");
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
