namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
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

        // Number of independent start attempts (each on a freshly chosen free port) before giving up.
        private const int _MaxStartAttempts = 5;

        // Per-attempt budget for the listener to begin accepting connections. Kept modest because a
        // genuinely-bound listener answers the health probe almost immediately; the retry loop, not a
        // long single wait, is what absorbs transient failures.
        private static readonly TimeSpan _PerAttemptReadyTimeout = TimeSpan.FromSeconds(6);

        private McpHttpServer? _Server = null;
        private CancellationTokenSource _Cts = new CancellationTokenSource();
        private Task? _RunTask = null;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The base URL for the HTTP MCP server. Populated once <see cref="StartAsync"/> succeeds.
        /// </summary>
        public string BaseUrl { get; private set; } = string.Empty;

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
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the HTTP MCP server, retrying on a fresh port if a start attempt fails to become ready.
        /// </summary>
        /// <returns>A task that completes once the listener is accepting connections.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no attempt becomes ready.</exception>
        public async Task StartAsync()
        {
            List<string> failures = new List<string>();

            for (int attempt = 1; attempt <= _MaxStartAttempts; attempt++)
            {
                int port = GetFreePort();
                string baseUrl = "http://127.0.0.1:" + port;
                McpHttpServer server = CreateServer(port);
                CancellationTokenSource cts = new CancellationTokenSource();

                // McpHttpServer.StartAsync runs an accept loop until cancellation, so this task normally
                // stays running for the server's lifetime. It also swallows startup exceptions internally
                // (e.g. a port grabbed between GetFreePort and bind), completing successfully without ever
                // listening — so completion *before* the health probe succeeds signals a failed start.
                Task runTask = Task.Run(() => server.StartAsync(cts.Token));

                string? failure = await WaitForReadyAsync(baseUrl, server, runTask, cts.Token).ConfigureAwait(false);
                if (failure == null)
                {
                    _Server = server;
                    _Cts = cts;
                    _RunTask = runTask;
                    BaseUrl = baseUrl;
                    return;
                }

                failures.Add("attempt " + attempt + " on " + baseUrl + ": " + failure);

                // Tear down the failed attempt before retrying on a new port.
                try { cts.Cancel(); } catch { /* best effort */ }
                try { server.Stop(); } catch { /* best effort */ }
                try { runTask.Wait(2000); } catch { /* ignore cancellation/stop errors */ }
                try { server.Dispose(); } catch { /* best effort */ }
                cts.Dispose();
            }

            throw new InvalidOperationException(
                "The HTTP MCP test server did not become ready after " + _MaxStartAttempts +
                " attempt(s). " + string.Join(" | ", failures));
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
            _Server?.Stop();
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
                _Server?.Dispose();
                _Cts.Dispose();
                _Disposed = true;
            }

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private McpHttpServer CreateServer(int port)
        {
            McpHttpServer server = new McpHttpServer("127.0.0.1", port);
            server.RegisterTool(
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

            return server;
        }

        /// <summary>
        /// Waits for a single start attempt to begin accepting connections.
        /// </summary>
        /// <returns><c>null</c> if the listener became ready; otherwise a description of why it did not.</returns>
        private static async Task<string?> WaitForReadyAsync(string baseUrl, McpHttpServer server, Task runTask, CancellationToken token)
        {
            // Short per-request timeout so a single stalled connect cannot eat the whole per-attempt budget.
            // Any HTTP response — even 404/405 — means the listener is accepting connections, which is all
            // "ready" requires here (the server answers GET / with a health response).
            using HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

            DateTime deadline = DateTime.UtcNow.Add(_PerAttemptReadyTimeout);
            while (DateTime.UtcNow < deadline)
            {
                // The run task faulting, or completing at all before we see a healthy response, both mean the
                // server exited without a live listener (McpHttpServer swallows startup errors and returns).
                if (runTask.IsFaulted)
                {
                    return "server task faulted: " +
                        (runTask.Exception?.GetBaseException().Message ?? "unknown error");
                }

                if (runTask.IsCompleted)
                {
                    return "server task exited before the listener became ready (startup error was swallowed by the server)";
                }

                try
                {
                    using HttpResponseMessage response = await client.GetAsync(baseUrl, token).ConfigureAwait(false);
                    return null;
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }

                try
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            if (runTask.IsFaulted)
            {
                return "server task faulted: " +
                    (runTask.Exception?.GetBaseException().Message ?? "unknown error");
            }

            if (runTask.IsCompleted)
            {
                return "server task exited before the listener became ready (startup error was swallowed by the server)";
            }

            return "timed out after " + _PerAttemptReadyTimeout.TotalSeconds + "s waiting for the listener to accept connections";
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
