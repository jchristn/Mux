namespace Test.Shared.Support
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// An in-process Voltaic MCP HTTP server whose tools each test defines, for exercising mux's MCP client against
    /// precise behavior (slow tools, failing tools, odd schemas, authentication). Records the authorization-related
    /// headers of every request so tests can check what mux sent.
    /// </summary>
    public sealed class ScriptableMcpHttpServer : IAsyncDisposable
    {
        #region Private-Members

        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private McpHttpServer? _Server;
        private Task _Running = Task.CompletedTask;

        #endregion

        #region Public-Members

        /// <summary>The base URL, for example <c>http://127.0.0.1:5123</c>.</summary>
        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>The port.</summary>
        public int Port { get; private set; }

        /// <summary>Every <c>Authorization</c> header received, in order (empty string when absent).</summary>
        public ConcurrentQueue<string> AuthorizationHeaders { get; } = new ConcurrentQueue<string>();

        /// <summary>Every <c>X-API-Key</c> (or other configured) header value received.</summary>
        public ConcurrentQueue<string> ApiKeyHeaders { get; } = new ConcurrentQueue<string>();

        /// <summary>The header name recorded into <see cref="ApiKeyHeaders"/>. Defaults to <c>X-API-Key</c>.</summary>
        public string ApiKeyHeaderName { get; set; } = "X-API-Key";

        /// <summary>When set, requests whose Authorization header differs are refused with 401.</summary>
        public string? RequiredAuthorization { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts a server on a free loopback port after letting the caller register tools.
        /// </summary>
        /// <param name="configure">Registers tools on the server.</param>
        /// <param name="required">Optional Authorization header value every request must carry.</param>
        /// <returns>The running server.</returns>
        public static async Task<ScriptableMcpHttpServer> StartAsync(Action<McpHttpServer> configure, string? required = null)
        {
            ScriptableMcpHttpServer fixture = new ScriptableMcpHttpServer { RequiredAuthorization = required };
            for (int attempt = 0; attempt < 5; attempt++)
            {
                int port = StubHttpServer.FreeLoopbackPort();
                McpHttpServer server = new McpHttpServer("127.0.0.1", port, includeDiagnosticTools: false);
                server.AuthenticationHandler = (HttpListenerRequest request) =>
                {
                    string auth = request.Headers["Authorization"] ?? string.Empty;
                    fixture.AuthorizationHeaders.Enqueue(auth);
                    fixture.ApiKeyHeaders.Enqueue(request.Headers[fixture.ApiKeyHeaderName] ?? string.Empty);
                    bool ok = fixture.RequiredAuthorization == null || string.Equals(auth, fixture.RequiredAuthorization, StringComparison.Ordinal);
                    AuthenticationResult result = new AuthenticationResult { IsAuthenticated = ok, StatusCode = ok ? 200 : 401, Principal = ok ? "test" : null, ErrorMessage = ok ? null : "bad credentials" };
                    return Task.FromResult(result);
                };
                configure(server);
                Task running = Task.Run(() => server.StartAsync(fixture._Stop.Token));
                if (await WaitReadyAsync("http://127.0.0.1:" + port, running).ConfigureAwait(false))
                {
                    fixture._Server = server;
                    fixture._Running = running;
                    fixture.Port = port;
                    fixture.BaseUrl = "http://127.0.0.1:" + port;
                    return fixture;
                }

                try { server.Stop(); server.Dispose(); } catch (Exception) { }
            }

            throw new InvalidOperationException("The scriptable MCP server did not start.");
        }

        /// <summary>
        /// Registers a tool that returns a fixed text.
        /// </summary>
        /// <param name="server">The server.</param>
        /// <param name="name">The tool name.</param>
        /// <param name="text">The text returned.</param>
        public static void Text(McpHttpServer server, string name, string text)
        {
            server.RegisterTool(name, "Returns " + text, new { type = "object", properties = new { } }, (RpcParameters args) => (object)text);
        }

        /// <summary>Stops the server.</summary>
        /// <returns>A task that completes when stopped.</returns>
        public async ValueTask DisposeAsync()
        {
            try { _Stop.Cancel(); } catch (Exception) { }
            try { _Server?.Stop(); } catch (Exception) { }
            try { await _Running.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); } catch (Exception) { }
            try { _Server?.Dispose(); } catch (Exception) { }
            _Stop.Dispose();
        }

        #endregion

        #region Private-Methods

        private static async Task<bool> WaitReadyAsync(string baseUrl, Task running)
        {
            using (HttpClient client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
            {
                for (int i = 0; i < 50; i++)
                {
                    if (running.IsCompleted) return false;
                    try
                    {
                        using (HttpResponseMessage response = await client.GetAsync(baseUrl).ConfigureAwait(false))
                        {
                            return true;
                        }
                    }
                    catch (Exception)
                    {
                        await Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
