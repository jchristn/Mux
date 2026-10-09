namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Skills;
    using Voltaic.Mcp;

    /// <summary>
    /// Runs <see cref="MuxMcpServerHost"/> over HTTP on a free loopback port for a test, and connects clients to it.
    /// </summary>
    public sealed class McpTestServer : IAsyncDisposable
    {
        #region Private-Members

        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private Task _Running = Task.CompletedTask;

        #endregion

        #region Public-Members

        /// <summary>The port the server listens on.</summary>
        public int Port { get; private set; }

        /// <summary>The server's MCP base URL (without the <c>/mcp</c> path).</summary>
        public string BaseUrl => "http://localhost:" + Port;

        /// <summary>The tools being served.</summary>
        public MuxMcpTools Tools { get; private set; } = null!;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts a server.
        /// </summary>
        /// <param name="options">The server options.</param>
        /// <param name="executor">The run executor.</param>
        /// <param name="endpoints">The endpoints to report.</param>
        /// <param name="sessions">The session store.</param>
        /// <param name="skills">The skills runtime, or null.</param>
        /// <param name="apiKey">The bearer key, or null.</param>
        /// <returns>The running server.</returns>
        public static async Task<McpTestServer> StartAsync(MuxMcpServerOptions options, IMcpRunExecutor executor, List<EndpointConfig> endpoints, SessionStore sessions, SkillRuntime? skills, string? apiKey)
        {
            McpTestServer server = new McpTestServer();
            server.Port = FreePort();
            server.Tools = new MuxMcpTools(options, executor, () => endpoints, sessions, skills);
            MuxMcpServerHost host = new MuxMcpServerHost(server.Tools, options);
            server._Running = Task.Run(() => host.RunHttpAsync("localhost", server.Port, apiKey, server._Stop.Token));
            for (int i = 0; i < 100; i++)
            {
                if (server._Running.IsFaulted) await server._Running.ConfigureAwait(false);
                try
                {
                    using (TcpClient probe = new TcpClient())
                    {
                        await probe.ConnectAsync("localhost", server.Port).ConfigureAwait(false);
                        return server;
                    }
                }
                catch (SocketException)
                {
                    await Task.Delay(50).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException("The MCP test server did not start.");
        }

        /// <summary>
        /// Connects a Voltaic Streamable HTTP client.
        /// </summary>
        /// <param name="bearer">The bearer key to send, or null.</param>
        /// <param name="token">Cancels the connect.</param>
        /// <returns>The connected client, or null when the server refused the connection.</returns>
        public async Task<McpHttpClient?> ConnectAsync(string? bearer, CancellationToken token)
        {
            McpHttpClient client = new McpHttpClient();
            if (bearer != null) client.SetRequestHeader("Authorization", "Bearer " + bearer);
            bool connected = await client.ConnectStreamableAsync(BaseUrl, MuxMcpServerHost.HttpPath, token).ConfigureAwait(false);
            if (!connected)
            {
                client.Dispose();
                return null;
            }

            return client;
        }

        /// <summary>
        /// Returns a free loopback port.
        /// </summary>
        /// <returns>The port.</returns>
        public static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            _Stop.Cancel();
            try { await _Running.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false); } catch (Exception) { }
            _Stop.Dispose();
        }

        #endregion
    }
}
