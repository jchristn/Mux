namespace Mux.Core.McpServer
{
    using System;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Serves <see cref="MuxMcpTools"/> over MCP with Voltaic: stdio (<see cref="RunStdioAsync"/>), which is what MCP
    /// clients launch, or Streamable HTTP (<see cref="RunHttpAsync"/>) at <c>/mcp</c>, loopback-only for loopback host
    /// names and optionally protected by a bearer key.
    /// </summary>
    public sealed class MuxMcpServerHost
    {
        #region Private-Members

        private readonly MuxMcpTools _Tools;
        private readonly MuxMcpServerOptions _Options;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxMcpServerHost"/> class.
        /// </summary>
        /// <param name="tools">The tools to serve. Must not be null.</param>
        /// <param name="options">The server options. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public MuxMcpServerHost(MuxMcpTools tools, MuxMcpServerOptions options)
        {
            _Tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        #endregion

        #region Public-Members

        /// <summary>The MCP path served over HTTP.</summary>
        public const string HttpPath = "/mcp";

        /// <summary>
        /// Instructions sent to clients in the handshake, telling the calling model what the server is for.
        /// </summary>
        public const string Instructions = "mux is an AI coding agent. Call run with a task to have mux do it (it reads and edits files and runs commands within the server's approval policy) and return the final answer. Use list_endpoints to pick a model, list_sessions and get_session to read past mux conversations, and list_skills to see mux's deterministic skills.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serves over stdin and stdout until the client disconnects or the token is cancelled. While it runs, Voltaic
        /// moves <see cref="Console.Out"/> to stderr so nothing else can corrupt the protocol stream.
        /// </summary>
        /// <param name="cancellationToken">Stops the server.</param>
        /// <returns>A task that completes when the server stops.</returns>
        public async Task RunStdioAsync(CancellationToken cancellationToken)
        {
            using (McpServer server = new McpServer(includeDiagnosticTools: false))
            {
                server.ServerName = _Options.ServerName;
                if (!string.IsNullOrEmpty(_Options.ServerVersion)) server.ServerVersion = _Options.ServerVersion;
                server.ServerInstructions = Instructions;
                server.IncludeToolExceptionMessages = false;
                _Tools.RegisterAll((string name, string description, object schema, Func<RpcParameters, CancellationToken, Task<object>> handler) =>
                    server.RegisterTool(name, description, schema, handler));
                await server.RunAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Serves Streamable HTTP at <c>http://host:port/mcp</c> until the token is cancelled.
        /// </summary>
        /// <param name="hostname">The host name to bind (for example <c>localhost</c>).</param>
        /// <param name="port">The port.</param>
        /// <param name="apiKey">When set, every request must send <c>Authorization: Bearer &lt;key&gt;</c>.</param>
        /// <param name="cancellationToken">Stops the server.</param>
        /// <returns>A task that completes when the server stops.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the port is outside 1..65535.</exception>
        public async Task RunHttpAsync(string hostname, int port, string? apiKey, CancellationToken cancellationToken)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port), "The port must be from 1 to 65535.");

            using (McpHttpServer server = new McpHttpServer(string.IsNullOrWhiteSpace(hostname) ? "localhost" : hostname, port, includeDiagnosticTools: false, mcpPath: HttpPath))
            {
                server.ServerName = _Options.ServerName;
                if (!string.IsNullOrEmpty(_Options.ServerVersion)) server.ServerVersion = _Options.ServerVersion;
                server.ServerInstructions = Instructions;
                server.IncludeToolExceptionMessages = false;
                if (!string.IsNullOrEmpty(apiKey))
                {
                    string expected = apiKey!;
                    server.AuthenticationHandler = (HttpListenerRequest request) => Task.FromResult(Authenticate(request.Headers["Authorization"], expected));
                }

                _Tools.RegisterAll((string name, string description, object schema, Func<RpcParameters, CancellationToken, Task<object>> handler) =>
                    server.RegisterTool(name, description, schema, handler));
                using (cancellationToken.Register(() => { try { server.Stop(); } catch (Exception) { } }))
                {
                    try
                    {
                        await server.StartAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Checks an <c>Authorization</c> header against the expected bearer key in constant time.
        /// </summary>
        /// <param name="header">The header value, or null.</param>
        /// <param name="expectedKey">The expected key.</param>
        /// <returns>An authenticated result, or a 401 result with a bearer challenge.</returns>
        public static AuthenticationResult Authenticate(string? header, string expectedKey)
        {
            string presented = string.Empty;
            if (!string.IsNullOrEmpty(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                presented = header.Substring(7).Trim();
            }

            byte[] a = Encoding.UTF8.GetBytes(presented);
            byte[] b = Encoding.UTF8.GetBytes(expectedKey ?? string.Empty);
            bool ok = a.Length > 0 && CryptographicOperations.FixedTimeEquals(a, b);
            AuthenticationResult result = new AuthenticationResult
            {
                IsAuthenticated = ok,
                Principal = ok ? "mcp-client" : null,
                StatusCode = ok ? 200 : 401,
                ErrorMessage = ok ? null : "A valid bearer key is required (Authorization: Bearer <key>)."
            };
            if (!ok)
            {
                result.Headers["WWW-Authenticate"] = "Bearer realm=\"mux\"";
            }

            return result;
        }

        #endregion
    }
}
