namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
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

        private Dictionary<string, McpClient> _Clients = new Dictionary<string, McpClient>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _ToolToServer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, List<ToolDefinition>> _ServerTools = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        private List<McpServerConfig> _Configs;
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
        /// Initializes all configured MCP servers by launching their processes, performing the
        /// MCP handshake, and discovering available tools.
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
                    await LaunchAndDiscoverAsync(config.Name, config.Command, config.Args, config.Env, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Server failed to launch — skip it and continue with remaining servers.
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
        /// <param name="toolName">The prefixed tool name (e.g. "serverName.toolName").</param>
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

            if (!_Clients.TryGetValue(serverName, out McpClient? client))
            {
                return new ToolResult
                {
                    ToolCallId = toolCallId,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "server_not_found", message = $"MCP server '{serverName}' is not connected." })
                };
            }

            // Strip the server prefix to get the original tool name
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
        /// Adds a new MCP server at runtime, launching its process and discovering tools.
        /// </summary>
        /// <param name="name">The unique name for the server.</param>
        /// <param name="command">The executable command to launch.</param>
        /// <param name="args">The command-line arguments.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task AddServerAsync(string name, string command, List<string> args, CancellationToken cancellationToken = default)
        {
            if (_Clients.ContainsKey(name))
            {
                throw new InvalidOperationException($"MCP server '{name}' is already registered.");
            }

            Dictionary<string, string> env = new Dictionary<string, string>();
            await LaunchAndDiscoverAsync(name, command, args, env, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Shuts down and removes an MCP server by name.
        /// </summary>
        /// <param name="name">The name of the server to remove.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RemoveServerAsync(string name)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            if (_Clients.TryGetValue(name, out McpClient? client))
            {
                client.Shutdown();
                client.Dispose();
                _Clients.Remove(name);
            }

            // Remove tool mappings for this server
            List<string> toolsToRemove = _ToolToServer
                .Where((KeyValuePair<string, string> kvp) => string.Equals(kvp.Value, name, StringComparison.OrdinalIgnoreCase))
                .Select((KeyValuePair<string, string> kvp) => kvp.Key)
                .ToList();

            foreach (string toolName in toolsToRemove)
            {
                _ToolToServer.Remove(toolName);
            }

            _ServerTools.Remove(name);
        }

        /// <summary>
        /// Returns the status of all managed MCP servers.
        /// </summary>
        /// <returns>A list of tuples containing server name, tool count, and connection status.</returns>
        public List<(string Name, int ToolCount, bool Connected)> GetServerStatus()
        {
            List<(string Name, int ToolCount, bool Connected)> status = new List<(string Name, int ToolCount, bool Connected)>();

            foreach (KeyValuePair<string, McpClient> kvp in _Clients)
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
                    foreach (KeyValuePair<string, McpClient> kvp in _Clients)
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

        private async Task LaunchAndDiscoverAsync(
            string name,
            string command,
            List<string> args,
            Dictionary<string, string> env,
            CancellationToken cancellationToken)
        {
            // Expand environment variables and set them before launching
            foreach (KeyValuePair<string, string> kvp in env)
            {
                string expandedValue = SettingsLoader.ExpandEnvironmentVariables(kvp.Value);
                Environment.SetEnvironmentVariable(kvp.Key, expandedValue);
            }

            McpClient client = new McpClient();

            string[] argsArray = args.ToArray();
            bool launched = await client.LaunchServerAsync(command, argsArray, cancellationToken).ConfigureAwait(false);

            if (!launched)
            {
                client.Dispose();
                throw new InvalidOperationException($"Failed to launch MCP server '{name}' with command: {command}");
            }

            _Clients[name] = client;

            // Discover tools via tools/list
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

                        string prefixedName = name + "." + toolName;

                        ToolDefinition definition = new ToolDefinition
                        {
                            Name = prefixedName,
                            Description = $"[MCP:{name}] {toolDescription}",
                            ParametersSchema = inputSchema
                        };

                        serverToolDefs.Add(definition);
                        _ToolToServer[prefixedName] = name;
                    }
                }

                _ServerTools[name] = serverToolDefs;
            }
            catch (Exception)
            {
                // Tool discovery failed — server is connected but has no tools.
                _ServerTools[name] = new List<ToolDefinition>();
            }
        }

        #endregion
    }
}
