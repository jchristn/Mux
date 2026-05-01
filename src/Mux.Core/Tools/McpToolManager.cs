namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Voltaic;
    using ToolDefinition = Mux.Core.Models.ToolDefinition;

    /// <summary>
    /// Manages connections to MCP (Model Context Protocol) servers, discovers their tools,
    /// and routes tool execution requests to the appropriate server.
    /// </summary>
    public class McpToolManager : IDisposable
    {
        #region Private-Members

        private readonly Dictionary<string, IMcpClientConnection> _Clients = new Dictionary<string, IMcpClientConnection>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _ToolToServer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<ToolDefinition>> _ServerTools = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<McpServerConfig> _Configs;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpToolManager"/> class with the specified server configurations.
        /// </summary>
        /// <param name="configs">The list of MCP server configurations to manage.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="configs"/> is null.</exception>
        public McpToolManager(List<McpServerConfig> configs)
        {
            _Configs = configs ?? throw new ArgumentNullException(nameof(configs));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initializes all configured MCP servers and discovers available tools.
        /// </summary>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous initialization operation.</returns>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            foreach (McpServerConfig config in _Configs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await ConnectAndDiscoverAsync(config, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Server failed to connect; skip it and continue with remaining servers.
                }
            }
        }

        /// <summary>
        /// Returns all discovered MCP tool definitions with server-prefixed names.
        /// </summary>
        /// <returns>A list of <see cref="ToolDefinition"/> objects for all connected MCP servers.</returns>
        public List<ToolDefinition> GetToolDefinitions()
        {
            List<ToolDefinition> definitions = new List<ToolDefinition>();

            foreach (KeyValuePair<string, List<ToolDefinition>> kvp in _ServerTools)
            {
                definitions.AddRange(kvp.Value);
            }

            return definitions;
        }

        /// <summary>
        /// Executes an MCP tool call by routing it to the appropriate server.
        /// </summary>
        /// <param name="toolCallId">The unique identifier for this tool call.</param>
        /// <param name="toolName">The prefixed tool name (for example "serverName.toolName").</param>
        /// <param name="arguments">The parsed JSON arguments from the LLM.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A <see cref="ToolResult"/> containing the execution output.</returns>
        public async Task<ToolResult> ExecuteAsync(string toolCallId, string toolName, JsonElement arguments, CancellationToken cancellationToken)
        {
            if (!_ToolToServer.TryGetValue(toolName, out string? serverName))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "unknown_mcp_tool", message = $"MCP tool '{toolName}' is not registered." })
                };
            }

            if (!_Clients.TryGetValue(serverName, out IMcpClientConnection? client))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "server_not_found", message = $"MCP server '{serverName}' is not connected." })
                };
            }

            string originalToolName = toolName;
            string prefix = serverName + ".";
            if (toolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                originalToolName = toolName.Substring(prefix.Length);
            }

            try
            {
                object callParams = new { name = originalToolName, arguments = arguments };
                JsonElement result = await client.CallAsync<JsonElement>("tools/call", callParams, 60000, cancellationToken).ConfigureAwait(false);

                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = true,
                    Content = result.GetRawText()
                };
            }
            catch (Exception ex)
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "mcp_call_failed", message = ex.Message })
                };
            }
        }

        /// <summary>
        /// Checks whether a tool with the specified prefixed name is registered.
        /// </summary>
        /// <param name="name">The prefixed tool name to look up.</param>
        /// <returns><c>true</c> if the tool exists; otherwise <c>false</c>.</returns>
        public bool HasTool(string name)
        {
            return _ToolToServer.ContainsKey(name);
        }

        /// <summary>
        /// Adds a new stdio MCP server at runtime and discovers its tools.
        /// </summary>
        /// <param name="name">The unique name for the server.</param>
        /// <param name="command">The executable command to launch.</param>
        /// <param name="args">The command-line arguments.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddServerAsync(string name, string command, List<string> args, CancellationToken cancellationToken = default)
        {
            await AddServerAsync(
                new McpServerConfig
                {
                    Name = name,
                    Transport = McpTransportTypeEnum.Stdio,
                    Command = command,
                    Args = new List<string>(args ?? new List<string>()),
                    Env = new Dictionary<string, string>()
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a new stdio MCP server at runtime and discovers its tools.
        /// </summary>
        /// <param name="name">The unique name for the server.</param>
        /// <param name="command">The executable command to launch.</param>
        /// <param name="args">The command-line arguments.</param>
        /// <param name="env">Environment variables to set for the server process.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddServerAsync(
            string name,
            string command,
            List<string> args,
            Dictionary<string, string> env,
            CancellationToken cancellationToken = default)
        {
            await AddServerAsync(
                new McpServerConfig
                {
                    Name = name,
                    Transport = McpTransportTypeEnum.Stdio,
                    Command = command,
                    Args = new List<string>(args ?? new List<string>()),
                    Env = new Dictionary<string, string>(env ?? new Dictionary<string, string>())
                },
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds a new MCP server at runtime and discovers its tools.
        /// </summary>
        /// <param name="config">The server configuration to add.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddServerAsync(McpServerConfig config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (_Clients.ContainsKey(config.Name))
            {
                throw new InvalidOperationException($"MCP server '{config.Name}' is already registered.");
            }

            McpServerConfig normalized = CloneConfig(config);
            await ConnectAndDiscoverAsync(normalized, cancellationToken).ConfigureAwait(false);
            _Configs.Add(normalized);
        }

        /// <summary>
        /// Disconnects and removes an MCP server by name.
        /// </summary>
        /// <param name="name">The name of the server to remove.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RemoveServerAsync(string name)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            if (_Clients.TryGetValue(name, out IMcpClientConnection? client))
            {
                client.Shutdown();
                client.Dispose();
                _Clients.Remove(name);
            }

            List<string> toolsToRemove = _ToolToServer
                .Where(kvp => string.Equals(kvp.Value, name, StringComparison.OrdinalIgnoreCase))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (string toolName in toolsToRemove)
            {
                _ToolToServer.Remove(toolName);
            }

            _ServerTools.Remove(name);
            _Configs.RemoveAll(config => string.Equals(config.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the status of all managed MCP servers.
        /// </summary>
        /// <returns>A list of tuples containing server name, tool count, and connection status.</returns>
        public List<(string Name, int ToolCount, bool Connected)> GetServerStatus()
        {
            List<(string Name, int ToolCount, bool Connected)> status = new List<(string Name, int ToolCount, bool Connected)>();

            foreach (KeyValuePair<string, IMcpClientConnection> kvp in _Clients)
            {
                int toolCount = 0;
                if (_ServerTools.TryGetValue(kvp.Key, out List<ToolDefinition>? tools))
                {
                    toolCount = tools.Count;
                }

                status.Add((kvp.Key, toolCount, kvp.Value.IsConnected));
            }

            return status;
        }

        /// <summary>
        /// Releases all resources used by this <see cref="McpToolManager"/> instance,
        /// shutting down all connected MCP servers.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Releases the unmanaged resources and optionally the managed resources.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_Disposed)
            {
                if (disposing)
                {
                    foreach (KeyValuePair<string, IMcpClientConnection> kvp in _Clients)
                    {
                        try
                        {
                            kvp.Value.Shutdown();
                            kvp.Value.Dispose();
                        }
                        catch (Exception)
                        {
                            // Swallow shutdown errors during disposal.
                        }
                    }

                    _Clients.Clear();
                    _ToolToServer.Clear();
                    _ServerTools.Clear();
                }

                _Disposed = true;
            }
        }

        private async Task ConnectAndDiscoverAsync(McpServerConfig config, CancellationToken cancellationToken)
        {
            IMcpClientConnection client = await ConnectClientAsync(config, cancellationToken).ConfigureAwait(false);
            _Clients[config.Name] = client;

            try
            {
                JsonElement toolsResult = await client.CallAsync<JsonElement>("tools/list", null, 30000, cancellationToken).ConfigureAwait(false);
                List<ToolDefinition> serverToolDefs = new List<ToolDefinition>();

                if (toolsResult.TryGetProperty("tools", out JsonElement toolsArray) && toolsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement toolElement in toolsArray.EnumerateArray())
                    {
                        string toolName = toolElement.GetProperty("name").GetString() ?? string.Empty;
                        string toolDescription = string.Empty;

                        if (toolElement.TryGetProperty("description", out JsonElement descElement))
                        {
                            toolDescription = descElement.GetString() ?? string.Empty;
                        }

                        object inputSchema = new object();
                        if (toolElement.TryGetProperty("inputSchema", out JsonElement schemaElement))
                        {
                            inputSchema = schemaElement;
                        }

                        string prefixedName = config.Name + "." + toolName;
                        ToolDefinition definition = new ToolDefinition
                        {
                            Name = prefixedName,
                            Description = $"[MCP:{config.Name}] {toolDescription}",
                            ParametersSchema = inputSchema
                        };

                        serverToolDefs.Add(definition);
                        _ToolToServer[prefixedName] = config.Name;
                    }
                }

                _ServerTools[config.Name] = serverToolDefs;
            }
            catch (Exception)
            {
                _ServerTools[config.Name] = new List<ToolDefinition>();
            }
        }

        private async Task<IMcpClientConnection> ConnectClientAsync(McpServerConfig config, CancellationToken cancellationToken)
        {
            return config.Transport switch
            {
                McpTransportTypeEnum.Http => await ConnectHttpClientAsync(config, cancellationToken).ConfigureAwait(false),
                _ => await ConnectStdioClientAsync(config, cancellationToken).ConfigureAwait(false)
            };
        }

        private async Task<IMcpClientConnection> ConnectStdioClientAsync(McpServerConfig config, CancellationToken cancellationToken)
        {
            foreach (KeyValuePair<string, string> kvp in config.Env)
            {
                string expandedValue = SettingsLoader.ExpandEnvironmentVariables(kvp.Value);
                Environment.SetEnvironmentVariable(kvp.Key, expandedValue);
            }

            McpClient client = new McpClient();
            bool launched = await client.LaunchServerAsync(config.Command, config.Args.ToArray(), cancellationToken).ConfigureAwait(false);
            if (!launched)
            {
                client.Dispose();
                throw new InvalidOperationException($"Failed to launch MCP server '{config.Name}' with command: {config.Command}");
            }

            return new StdioMcpClientConnection(client);
        }

        private async Task<IMcpClientConnection> ConnectHttpClientAsync(McpServerConfig config, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(config.Url, UriKind.Absolute, out Uri? uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("HTTP MCP servers require an absolute http:// or https:// URL.");
            }

            McpHttpClient client = new McpHttpClient();
            bool connected = await client.ConnectStreamableAsync(config.Url, NormalizeMcpPath(config.McpPath), cancellationToken).ConfigureAwait(false);
            if (!connected)
            {
                client.Dispose();
                throw new InvalidOperationException($"Failed to connect to HTTP MCP server '{config.Name}' at {config.Url}");
            }

            return new HttpMcpClientConnection(client);
        }

        private static McpServerConfig CloneConfig(McpServerConfig source)
        {
            return new McpServerConfig
            {
                Name = source.Name,
                Transport = source.Transport,
                Command = source.Command,
                Args = new List<string>(source.Args ?? new List<string>()),
                Env = new Dictionary<string, string>(source.Env ?? new Dictionary<string, string>()),
                Url = source.Url,
                McpPath = NormalizeMcpPath(source.McpPath)
            };
        }

        private static string NormalizeMcpPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "/mcp";
            }

            string normalized = path.Trim();
            return normalized.StartsWith("/", StringComparison.Ordinal) ? normalized : "/" + normalized;
        }

        #endregion

        #region Private-Types

        private interface IMcpClientConnection : IDisposable
        {
            bool IsConnected { get; }

            Task<T> CallAsync<T>(string method, object? parameters, int timeoutMs, CancellationToken cancellationToken);

            void Shutdown();
        }

        private sealed class StdioMcpClientConnection : IMcpClientConnection
        {
            private readonly McpClient _Client;

            public StdioMcpClientConnection(McpClient client)
            {
                _Client = client;
            }

            public bool IsConnected => _Client.IsConnected;

            public Task<T> CallAsync<T>(string method, object? parameters, int timeoutMs, CancellationToken cancellationToken)
            {
                return _Client.CallAsync<T>(method, parameters, timeoutMs, cancellationToken);
            }

            public void Shutdown()
            {
                _Client.Shutdown();
            }

            public void Dispose()
            {
                _Client.Dispose();
            }
        }

        private sealed class HttpMcpClientConnection : IMcpClientConnection
        {
            private readonly McpHttpClient _Client;

            public HttpMcpClientConnection(McpHttpClient client)
            {
                _Client = client;
            }

            public bool IsConnected => _Client.IsConnected;

            public Task<T> CallAsync<T>(string method, object? parameters, int timeoutMs, CancellationToken cancellationToken)
            {
                return _Client.CallAsync<T>(method, parameters, timeoutMs, cancellationToken);
            }

            public void Shutdown()
            {
                _Client.Disconnect();
            }

            public void Dispose()
            {
                _Client.Dispose();
            }
        }

        #endregion
    }
}
