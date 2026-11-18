namespace Mux.Server.Cloud
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Cloud;
    using Mux.Core.Models;
    using Mux.Core.Runs;
    using Mux.Core.Settings;
    using MuxCloud.Protocol;

    /// <summary>
    /// Runs this machine's Mux Cloud tunnel on behalf of the process that owns the local hub port (the tray agent,
    /// <c>mux serve</c>, or the desktop app's embedded server). It takes the config-directory lock so only one process
    /// per machine runs a tunnel, watches <c>settings.json</c> so signing in or out from any surface applies without a
    /// restart, keeps the server's <see cref="CloudRoutePolicy"/> current, and pushes <c>machine.status</c> frames
    /// (active runs, approvals waiting, run finished) without conversation content.
    /// <para>Thread safety: safe to call from any thread.</para>
    /// </summary>
    public sealed class CloudTunnelHost : IDisposable
    {
        #region Private-Members

        private readonly MuxServer _Server;
        private readonly string _ConfigDirectory;
        private readonly string _MuxVersion;
        private readonly Func<MuxSettings> _LoadSettings;
        private readonly object _Sync = new object();
        private readonly CloudTunnelLock _Lock;
        private CloudTunnelClient? _Client;
        private CloudRelayForwarder? _Forwarder;
        private string _ClientKey = string.Empty;
        private CloudSettings _Current = new CloudSettings();
        private FileSystemWatcher? _Watcher;
        private Timer? _Debounce;
        private Timer? _StatusTimer;
        private CloudStatus _IdleStatus = new CloudStatus();
        private bool _Started;
        private bool _Disposed;

        #endregion

        #region Public-Events

        /// <summary>Raised with a snapshot whenever the status changes. Handlers must not throw.</summary>
        public event Action<CloudStatus>? StatusChanged;

        #endregion

        #region Public-Members

        /// <summary>A snapshot of the current status.</summary>
        public CloudStatus Status
        {
            get
            {
                lock (_Sync)
                {
                    CloudStatus status = _Client != null ? _Client.Status : _IdleStatus.Clone();
                    status.AllowRemoteToolApproval = _Current.AllowRemoteToolApproval;
                    return status;
                }
            }
        }

        /// <summary>
        /// Heartbeat interval override passed to the tunnel client (tests). Null uses the server's value.
        /// </summary>
        public TimeSpan? HeartbeatIntervalOverride { get; set; } = null;

        /// <summary>First reconnect delay in seconds passed to the tunnel client. Default 2.</summary>
        public int ReconnectBaseDelaySeconds { get; set; } = 2;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="server">The started local hub.</param>
        /// <param name="muxVersion">Installed mux version.</param>
        /// <param name="configDirectory">Config directory (lock file and settings.json); null uses the current one.</param>
        /// <param name="loadSettings">Settings loader; null uses <see cref="SettingsLoader.LoadSettings"/>.</param>
        /// <exception cref="ArgumentNullException">server is null.</exception>
        public CloudTunnelHost(MuxServer server, string muxVersion, string? configDirectory = null, Func<MuxSettings>? loadSettings = null)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _MuxVersion = string.IsNullOrWhiteSpace(muxVersion) ? CloudMachineInfo.MuxVersion() : muxVersion;
            _ConfigDirectory = string.IsNullOrWhiteSpace(configDirectory) ? SettingsLoader.GetConfigDirectory() : configDirectory;
            _LoadSettings = loadSettings ?? SettingsLoader.LoadSettings;
            _Lock = new CloudTunnelLock(_ConfigDirectory);
            _Server.CloudHost = this;
        }

        #endregion

        #region Public-Methods

        /// <summary>Read settings, connect when enrolled and enabled, and start watching settings.json. Idempotent.</summary>
        public void Start()
        {
            lock (_Sync)
            {
                if (_Started || _Disposed) return;
                _Started = true;
            }

            _Server.Runs.RunRegistered += OnRunRegistered;
            Refresh();
            try
            {
                Directory.CreateDirectory(_ConfigDirectory);
                FileSystemWatcher watcher = new FileSystemWatcher(_ConfigDirectory, "settings.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime
                };
                watcher.Changed += (_, _) => ScheduleRefresh();
                watcher.Created += (_, _) => ScheduleRefresh();
                watcher.Renamed += (_, _) => ScheduleRefresh();
                watcher.EnableRaisingEvents = true;
                lock (_Sync) { _Watcher = watcher; }
            }
            catch (Exception)
            {
                // Without a watcher, settings changes apply on the next Refresh (login and logout routes call it).
            }

            _StatusTimer = new Timer(_ => { _ = PushStatusAsync(MachineStatusKinds.Status, null); }, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        }

        /// <summary>
        /// Re-read settings and converge: connect, reconnect with a new credential or URL, or disconnect. Safe to call
        /// at any time (the login and logout routes call it so the change is immediate).
        /// </summary>
        public void Refresh()
        {
            MuxSettings settings;
            try
            {
                settings = _LoadSettings();
            }
            catch (Exception e)
            {
                SetIdle(CloudAccountService.OfflineStatus(_Current, "Could not read settings.json: " + e.Message));
                return;
            }

            CloudSettings cloud = settings.Cloud;
            CloudTunnelClient? stop = null;
            CloudRelayForwarder? stopForwarder = null;
            CloudTunnelClient? start = null;
            lock (_Sync)
            {
                if (_Disposed) return;
                _Current = cloud;
                _Server.CloudPolicy.AllowRemoteToolApproval = cloud.AllowRemoteToolApproval;
                bool want = cloud.Enabled && cloud.IsEnrolled;
                string key = want ? string.Join("|", cloud.ServerUrl, cloud.MachineId, cloud.MachineSecret, cloud.MachineName, cloud.AllowInvalidCertificates, cloud.ReconnectMaxDelaySeconds) : string.Empty;

                if (!want || key != _ClientKey)
                {
                    stop = _Client;
                    stopForwarder = _Forwarder;
                    _Client = null;
                    _Forwarder = null;
                    _ClientKey = string.Empty;
                }

                if (!want)
                {
                    _Lock.Release();
                    _Server.CloudPolicy.CloudConnected = false;
                    _IdleStatus = CloudAccountService.OfflineStatus(cloud, null);
                    _IdleStatus.HostedHere = true;
                }
                else if (_Client == null)
                {
                    if (!_Lock.TryAcquire())
                    {
                        _IdleStatus = CloudAccountService.OfflineStatus(cloud, "Another mux process on this machine is running the Mux Cloud tunnel.");
                        _Server.CloudPolicy.CloudConnected = false;
                    }
                    else
                    {
                        CloudRelayForwarder forwarder = new CloudRelayForwarder(BuildEndpoint);
                        CloudTunnelClient client = new CloudTunnelClient(new CloudTunnelOptions
                        {
                            ServerUrl = cloud.ServerUrl,
                            MachineId = cloud.MachineId ?? string.Empty,
                            MachineSecret = cloud.MachineSecret ?? string.Empty,
                            MachineName = cloud.MachineName ?? CloudMachineInfo.Hostname(),
                            AccountEmail = cloud.AccountEmail ?? string.Empty,
                            MuxVersion = _MuxVersion,
                            AllowRemoteToolApproval = cloud.AllowRemoteToolApproval,
                            AllowInvalidCertificates = cloud.AllowInvalidCertificates,
                            HeartbeatSeconds = cloud.HeartbeatSeconds,
                            HeartbeatIntervalOverride = HeartbeatIntervalOverride,
                            ReconnectBaseDelaySeconds = ReconnectBaseDelaySeconds,
                            ReconnectMaxDelaySeconds = cloud.ReconnectMaxDelaySeconds
                        }, forwarder);
                        client.StatusChanged += OnClientStatus;
                        _Client = client;
                        _Forwarder = forwarder;
                        _ClientKey = key;
                        start = client;
                    }
                }
            }

            if (stop != null)
            {
                stop.StatusChanged -= OnClientStatus;
                try { stop.StopAsync().GetAwaiter().GetResult(); } catch (Exception) { }
                stop.Dispose();
            }

            stopForwarder?.Dispose();
            start?.Start();
            if (start == null) RaiseStatus(Status);
        }

        /// <summary>Stop the tunnel, release the lock, and stop watching settings.</summary>
        public void Dispose()
        {
            CloudTunnelClient? client;
            CloudRelayForwarder? forwarder;
            lock (_Sync)
            {
                if (_Disposed) return;
                _Disposed = true;
                client = _Client;
                forwarder = _Forwarder;
                _Client = null;
                _Forwarder = null;
                try { _Watcher?.Dispose(); } catch (Exception) { }
                _Watcher = null;
                _Debounce?.Dispose();
                _StatusTimer?.Dispose();
            }

            _Server.Runs.RunRegistered -= OnRunRegistered;
            if (client != null)
            {
                client.StatusChanged -= OnClientStatus;
                client.Dispose();
            }

            forwarder?.Dispose();
            _Lock.Dispose();
            _Server.CloudPolicy.CloudConnected = false;
            if (ReferenceEquals(_Server.CloudHost, this)) _Server.CloudHost = null;
        }

        #endregion

        #region Private-Methods

        private CloudLocalEndpoint BuildEndpoint()
        {
            CloudSettings current;
            lock (_Sync) { current = _Current; }
            return new CloudLocalEndpoint
            {
                BaseUrl = _Server.LoopbackUrl,
                ApiKey = _Server.Settings.ApiKey,
                MachineId = current.MachineId ?? string.Empty
            };
        }

        private void ScheduleRefresh()
        {
            lock (_Sync)
            {
                if (_Disposed) return;
                _Debounce?.Dispose();
                _Debounce = new Timer(_ => { try { Refresh(); } catch (Exception) { } }, null, TimeSpan.FromMilliseconds(400), Timeout.InfiniteTimeSpan);
            }
        }

        private void OnClientStatus(CloudStatus status)
        {
            bool connected = status.State == CloudStateEnum.Connected;
            bool wasConnected = _Server.CloudPolicy.CloudConnected;
            _Server.CloudPolicy.CloudConnected = connected;
            RaiseStatus(status);
            if (connected && !wasConnected) _ = PushStatusAsync(MachineStatusKinds.Status, null);
        }

        private void SetIdle(CloudStatus status)
        {
            lock (_Sync) { _IdleStatus = status; }
            RaiseStatus(status);
        }

        private void RaiseStatus(CloudStatus status)
        {
            try { StatusChanged?.Invoke(status); }
            catch (Exception) { /* listeners must never break the host */ }
        }

        private void OnRunRegistered(RunHandle handle)
        {
            handle.ApprovalRequested += (h, _, _) => { _ = PushStatusAsync(MachineStatusKinds.ApprovalRequested, h); };
            handle.Completed += () => { _ = PushStatusAsync(MachineStatusKinds.RunCompleted, handle); };
            _ = PushStatusAsync(MachineStatusKinds.Status, null);
        }

        private async Task PushStatusAsync(string kind, RunHandle? run)
        {
            CloudTunnelClient? client;
            CloudSettings current;
            lock (_Sync)
            {
                client = _Client;
                current = _Current;
            }

            if (client == null || !client.IsConnected) return;
            try
            {
                System.Collections.Generic.IReadOnlyList<RunHandle> runs = _Server.Runs.List();
                MachineStatusPayload payload = new MachineStatusPayload
                {
                    Kind = kind,
                    MuxVersion = _MuxVersion,
                    MachineName = client.Status.MachineName,
                    ActiveRuns = runs.Count(r => !r.IsTerminal),
                    AwaitingApproval = runs.Count(r => !r.IsTerminal && r.PendingApprovalCount > 0),
                    RemoteToolApproval = current.AllowRemoteToolApproval,
                    SessionId = run?.SessionId ?? string.Empty,
                    RunId = run?.RunId ?? string.Empty
                };
                await client.SendMachineStatusAsync(payload, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Status pushes are best effort.
            }
        }

        #endregion
    }
}
