namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Owns the lifecycle of the interactive session's MCP (Model Context Protocol) connections. On start it
    /// connects to every configured server and discovers its tools; on a periodic timer it re-validates
    /// connectivity and, when a server is down or the configuration changed, rebuilds the connections. The
    /// discovered tools and per-server status are exposed for the shell to render and for the agent template
    /// to consume; <see cref="ExecuteToolAsync"/> routes an MCP tool call to the right server.
    ///
    /// <para>Thread-safety follows an immutable-swap model: a refresh builds a brand-new <see cref="McpToolManager"/>,
    /// fully initializes it off to the side, then atomically swaps it in. A published manager is only ever read
    /// concurrently (tool execution and status), never mutated, so no locking of the manager internals is
    /// required.</para>
    /// </summary>
    public sealed class McpRuntime : IDisposable
    {
        #region Private-Members

        // When a server is down or failed to connect, only retry a full reconnect every Nth validation tick
        // so a permanently-broken server does not churn the healthy connections on every tick.
        private const int ReconnectEveryTicks = 4;

        private readonly Func<List<McpServerConfig>> _LoadConfigs;
        private readonly Action _OnToolsChanged;
        private readonly Action<string>? _OnNotice;
        private readonly TimeSpan _Interval;
        private readonly SemaphoreSlim _Gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly object _Sync = new object();
        private readonly TaskCompletionSource<bool> _FirstRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        private McpToolManager? _Manager;
        private List<ToolDefinition> _Tools = new List<ToolDefinition>();
        private List<McpServerStatus> _Status = new List<McpServerStatus>();
        private string _ConfigSignature = string.Empty;
        private int _TicksSinceReconnect;
        private Task? _Loop;
        private bool _Disposed;

        // The last connection notice emitted per server, so a periodic reconnect that produces the same
        // outcome does not repeatedly re-announce it. Keyed by server name; the value is the emitted message.
        private readonly Dictionary<string, string> _LastNotified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="McpRuntime"/> class.
        /// </summary>
        /// <param name="loadConfigs">Loads the current MCP server configurations (the source of truth). Must not be null.</param>
        /// <param name="onToolsChanged">Invoked (off the UI thread) after a refresh that changes the discovered tool set. Must not be null.</param>
        /// <param name="interval">The connectivity-validation interval. Defaults to 30 seconds when null.</param>
        /// <param name="onNotice">Optional callback (invoked off the UI thread) that receives a human-readable notice whenever a server's connect outcome changes — a success with its tool count, or a failure with the transport and error details. Null suppresses notices.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public McpRuntime(Func<List<McpServerConfig>> loadConfigs, Action onToolsChanged, TimeSpan? interval = null, Action<string>? onNotice = null)
        {
            _LoadConfigs = loadConfigs ?? throw new ArgumentNullException(nameof(loadConfigs));
            _OnToolsChanged = onToolsChanged ?? throw new ArgumentNullException(nameof(onToolsChanged));
            _Interval = interval ?? TimeSpan.FromSeconds(30);
            _OnNotice = onNotice;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// A snapshot of the currently discovered MCP tool definitions. Never null.
        /// </summary>
        public List<ToolDefinition> CurrentTools
        {
            get
            {
                lock (_Sync)
                {
                    return new List<ToolDefinition>(_Tools);
                }
            }
        }

        /// <summary>
        /// A task that completes after the first refresh (initial connect + discovery) finishes.
        /// </summary>
        public Task FirstRefreshCompleted
        {
            get { return _FirstRefresh.Task; }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the background connect + periodic-validation loop. Safe to call once.
        /// </summary>
        public void Start()
        {
            lock (_Sync)
            {
                if (_Loop != null || _Disposed)
                {
                    return;
                }

                _Loop = Task.Run(() => LoopAsync(_Cts.Token));
            }
        }

        /// <summary>
        /// Returns the per-server connectivity status for every configured server. Never null.
        /// </summary>
        /// <returns>A detached copy of the current status snapshot.</returns>
        public List<McpServerStatus> GetStatus()
        {
            lock (_Sync)
            {
                List<McpServerStatus> copy = new List<McpServerStatus>(_Status.Count);
                foreach (McpServerStatus status in _Status)
                {
                    copy.Add(new McpServerStatus { Name = status.Name, ToolCount = status.ToolCount, Connected = status.Connected });
                }

                return copy;
            }
        }

        /// <summary>
        /// Requests an immediate full refresh (reconnect + rediscover), for example after the configuration
        /// was edited. Fire-and-forget; the refresh is serialized with the periodic loop.
        /// </summary>
        public void RequestRefresh()
        {
            if (_Disposed)
            {
                return;
            }

            _ = RefreshAsync(force: true, _Cts.Token);
        }

        /// <summary>
        /// Executes an MCP tool call by routing it to the connected server that owns it. Matches the
        /// <see cref="Mux.Core.Agent.AgentLoopOptions.ExternalToolExecutor"/> delegate shape.
        /// </summary>
        /// <param name="toolName">The prefixed MCP tool name (for example "serverName.toolName").</param>
        /// <param name="arguments">The parsed JSON arguments from the model.</param>
        /// <param name="workingDirectory">The working directory (unused by MCP; part of the executor contract).</param>
        /// <param name="cancellationToken">A token to cancel the call.</param>
        /// <returns>The tool result, or an error result when the tool is unknown.</returns>
        public async Task<ToolResult> ExecuteToolAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            McpToolManager? manager;
            lock (_Sync)
            {
                manager = _Manager;
            }

            if (manager == null || !manager.HasTool(toolName))
            {
                return new ToolResult
                {
                    ToolCallId = toolName,
                    Success = false,
                    Content = JsonSerializer.Serialize(new { error = "unknown_mcp_tool", message = $"MCP tool '{toolName}' is not available." })
                };
            }

            return await manager.ExecuteAsync(toolName, toolName, arguments, cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            McpToolManager? manager;
            lock (_Sync)
            {
                if (_Disposed)
                {
                    return;
                }

                _Disposed = true;
                manager = _Manager;
                _Manager = null;
            }

            _Cts.Cancel();
            try { _Loop?.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            try { manager?.Dispose(); } catch (Exception) { }
            _Cts.Dispose();
            _Gate.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken cancellationToken)
        {
            await RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_Interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                await RefreshAsync(force: false, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task RefreshAsync(bool force, CancellationToken cancellationToken)
        {
            try
            {
                await _Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                if (cancellationToken.IsCancellationRequested || _Disposed)
                {
                    return;
                }

                List<McpServerConfig> configs;
                try
                {
                    configs = _LoadConfigs() ?? new List<McpServerConfig>();
                }
                catch (Exception)
                {
                    configs = new List<McpServerConfig>();
                }

                string signature = ComputeSignature(configs);
                bool configChanged = !string.Equals(signature, _ConfigSignature, StringComparison.Ordinal);

                if (!force && !configChanged)
                {
                    // Steady state: validate connectivity from the live manager without reconnecting, unless
                    // a server is down and we are due for a bounded reconnect attempt.
                    List<McpServerStatus> validated = SnapshotStatus(configs);
                    bool anyDown = AnyDown(validated);

                    if (anyDown && ++_TicksSinceReconnect >= ReconnectEveryTicks)
                    {
                        _TicksSinceReconnect = 0;
                    }
                    else
                    {
                        lock (_Sync)
                        {
                            _Status = validated;
                        }

                        return;
                    }
                }

                // Full (re)initialization: build a fresh manager off to the side, then swap it in.
                McpToolManager candidate = new McpToolManager(CloneConfigs(configs));
                try
                {
                    await candidate.InitializeAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    candidate.Dispose();
                    return;
                }
                catch (Exception)
                {
                    // InitializeAsync already swallows per-server failures; any other error leaves the
                    // candidate usable (possibly with no tools).
                }

                List<ToolDefinition> tools = candidate.GetToolDefinitions();
                List<McpServerStatus> status = BuildStatus(configs, candidate);

                // Surface a per-server success/failure notice for any server whose outcome changed since we
                // last announced it. Done before the swap so the message reflects the manager we just built.
                EmitConnectionNotices(configs, candidate.GetConnectionResults());

                McpToolManager? previous;
                lock (_Sync)
                {
                    previous = _Manager;
                    _Manager = candidate;
                    _Tools = tools;
                    _Status = status;
                    _ConfigSignature = signature;
                }

                try { previous?.Dispose(); } catch (Exception) { }

                // A reconnect attempt just happened; restart the bounded-retry counter.
                _TicksSinceReconnect = 0;

                _FirstRefresh.TrySetResult(true);

                try
                {
                    _OnToolsChanged();
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                if (!_Disposed)
                {
                    try { _Gate.Release(); } catch (Exception) { }
                }
            }
        }

        // Emits a notice for each server whose connect outcome changed since it was last announced, and forgets
        // servers that are no longer configured so re-adding one announces again. No-op when no notice sink is
        // wired (for example, tests).
        private void EmitConnectionNotices(List<McpServerConfig> configs, List<McpConnectionResult> results)
        {
            if (_OnNotice == null)
            {
                return;
            }

            HashSet<string> configured = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (McpServerConfig config in configs)
            {
                configured.Add(config.Name);
            }

            foreach (McpConnectionResult result in results)
            {
                string message = result.Connected
                    ? $"Connected successfully to MCP server {result.Name}: {result.ToolCount} tools"
                    : $"Unable to connect to MCP server {result.Name} using {result.Method}: {result.Error}";

                if (_LastNotified.TryGetValue(result.Name, out string? previous) && string.Equals(previous, message, StringComparison.Ordinal))
                {
                    continue;
                }

                _LastNotified[result.Name] = message;

                try
                {
                    _OnNotice(message);
                }
                catch (Exception)
                {
                }
            }

            // Drop remembered notices for servers that have been removed from the configuration.
            List<string> stale = new List<string>();
            foreach (string name in _LastNotified.Keys)
            {
                if (!configured.Contains(name))
                {
                    stale.Add(name);
                }
            }

            foreach (string name in stale)
            {
                _LastNotified.Remove(name);
            }
        }

        // Rebuilds the status list from the current (published) manager's live connectivity.
        private List<McpServerStatus> SnapshotStatus(List<McpServerConfig> configs)
        {
            McpToolManager? manager;
            lock (_Sync)
            {
                manager = _Manager;
            }

            return manager == null ? OfflineStatus(configs) : BuildStatus(configs, manager);
        }

        private static List<McpServerStatus> BuildStatus(List<McpServerConfig> configs, McpToolManager manager)
        {
            Dictionary<string, McpServerStatus> live = new Dictionary<string, McpServerStatus>(StringComparer.OrdinalIgnoreCase);
            foreach (McpServerStatus status in manager.GetServerStatus())
            {
                live[status.Name] = status;
            }

            List<McpServerStatus> result = new List<McpServerStatus>();
            foreach (McpServerConfig config in configs)
            {
                if (live.TryGetValue(config.Name, out McpServerStatus? status))
                {
                    result.Add(new McpServerStatus { Name = config.Name, Connected = status.Connected, ToolCount = status.ToolCount });
                }
                else
                {
                    result.Add(new McpServerStatus { Name = config.Name, Connected = false, ToolCount = 0 });
                }
            }

            return result;
        }

        private static List<McpServerStatus> OfflineStatus(List<McpServerConfig> configs)
        {
            List<McpServerStatus> result = new List<McpServerStatus>();
            foreach (McpServerConfig config in configs)
            {
                result.Add(new McpServerStatus { Name = config.Name, Connected = false, ToolCount = 0 });
            }

            return result;
        }

        private static bool AnyDown(List<McpServerStatus> status)
        {
            foreach (McpServerStatus entry in status)
            {
                if (!entry.Connected)
                {
                    return true;
                }
            }

            return false;
        }

        private static string ComputeSignature(List<McpServerConfig> configs)
        {
            try
            {
                return JsonSerializer.Serialize(configs);
            }
            catch (Exception)
            {
                return Guid.NewGuid().ToString();
            }
        }

        private static List<McpServerConfig> CloneConfigs(List<McpServerConfig> configs)
        {
            List<McpServerConfig> clones = new List<McpServerConfig>(configs.Count);
            foreach (McpServerConfig config in configs)
            {
                clones.Add(new McpServerConfig
                {
                    Name = config.Name,
                    Transport = config.Transport,
                    Command = config.Command,
                    Args = new List<string>(config.Args ?? new List<string>()),
                    Env = new Dictionary<string, string>(config.Env ?? new Dictionary<string, string>()),
                    Url = config.Url,
                    McpPath = config.McpPath,
                    Auth = CloneAuth(config.Auth)
                });
            }

            return clones;
        }

        private static McpAuthConfig CloneAuth(McpAuthConfig? source)
        {
            if (source == null)
            {
                return new McpAuthConfig();
            }

            return new McpAuthConfig
            {
                Type = source.Type,
                BearerToken = source.BearerToken,
                ApiKeyHeader = source.ApiKeyHeader,
                ApiKeyValue = source.ApiKeyValue
            };
        }

        #endregion
    }
}
