namespace Mux.Core.Telemetry
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// A per-process facade that owns the usage-telemetry store and its background recorder and hands out
    /// an <see cref="IUsageRecorder"/> to wire into agent runs. Construction is best-effort and never
    /// throws: when telemetry is disabled or the database cannot be opened, <see cref="Recorder"/> is a
    /// no-op recorder and the rest of the product is unaffected. Create one instance at process start and
    /// dispose it at shutdown to flush buffered events.
    /// </summary>
    public sealed class UsageTelemetry : IAsyncDisposable, IDisposable
    {
        #region Private-Members

        private readonly SqliteUsageStore? _Store;
        private readonly SqliteUsageRecorder? _Recorder;
        private readonly IUsageRecorder _Effective;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The recorder to attach to agent runs. Never null; a no-op recorder when telemetry is disabled or
        /// unavailable.
        /// </summary>
        public IUsageRecorder Recorder
        {
            get => _Effective;
        }

        /// <summary>
        /// Whether durable recording is active (false when telemetry is disabled or the store failed to open).
        /// </summary>
        public bool Enabled
        {
            get => _Recorder != null;
        }

        /// <summary>
        /// The resolved database path when enabled, or null.
        /// </summary>
        public string? DatabasePath
        {
            get => _Store?.DatabasePath;
        }

        #endregion

        #region Constructors-and-Factories

        private UsageTelemetry()
        {
            _Effective = NullUsageRecorder.Instance;
        }

        private UsageTelemetry(SqliteUsageStore store, SqliteUsageRecorder recorder)
        {
            _Store = store;
            _Recorder = recorder;
            _Effective = recorder;
        }

        /// <summary>
        /// Creates a usage-telemetry facade from settings. Best-effort: returns a disabled facade (no-op
        /// recorder) when telemetry is off in settings or the database cannot be opened.
        /// </summary>
        /// <param name="settings">The effective mux settings. When null, telemetry is disabled.</param>
        /// <param name="configDirectory">The effective config directory used to resolve the default database path.</param>
        /// <param name="logger">An optional sink for best-effort diagnostics. Null discards them.</param>
        /// <returns>A <see cref="UsageTelemetry"/> instance; never null.</returns>
        public static UsageTelemetry Create(MuxSettings? settings, string configDirectory, Action<string>? logger)
        {
            if (settings == null || settings.Telemetry == null || !settings.Telemetry.Enabled)
            {
                return new UsageTelemetry();
            }

            try
            {
                string databasePath = ResolveDatabasePath(settings.Telemetry, configDirectory);
                SqliteUsageStore store = new SqliteUsageStore(
                    databasePath,
                    settings.Telemetry.RetentionDays,
                    settings.Telemetry.MaxRows);
                SqliteUsageRecorder recorder = new SqliteUsageRecorder(store, logger);
                return new UsageTelemetry(store, recorder);
            }
            catch (Exception ex)
            {
                logger?.Invoke("usage telemetry disabled: " + ex.Message);
                return new UsageTelemetry();
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a read-side query service over the underlying store, or null when telemetry is disabled
        /// (no store to query).
        /// </summary>
        /// <param name="pricingProvider">A provider of the current pricing table, called per query. Required.</param>
        /// <returns>A <see cref="UsageQueryService"/> when enabled; otherwise null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pricingProvider"/> is null.</exception>
        public UsageQueryService? CreateQueryService(Func<PricingTable> pricingProvider)
        {
            if (pricingProvider == null)
            {
                throw new ArgumentNullException(nameof(pricingProvider));
            }

            return _Store == null ? null : new UsageQueryService(_Store, pricingProvider);
        }

        /// <summary>
        /// Flushes buffered events. Best-effort; never throws.
        /// </summary>
        /// <param name="token">A token to cancel the flush.</param>
        /// <returns>A task that completes when buffered events have been drained.</returns>
        public Task FlushAsync(CancellationToken token)
        {
            return _Effective.FlushAsync(token);
        }

        /// <summary>
        /// Flushes and disposes the recorder and store.
        /// </summary>
        /// <returns>A task that completes when shutdown is finished.</returns>
        public async ValueTask DisposeAsync()
        {
            if (_Disposed)
            {
                return;
            }

            _Disposed = true;

            if (_Recorder != null)
            {
                await _Recorder.DisposeAsync().ConfigureAwait(false);
            }

            _Store?.Dispose();
        }

        /// <summary>
        /// Flushes and disposes the recorder and store synchronously.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed)
            {
                return;
            }

            DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        #endregion

        #region Private-Methods

        private static string ResolveDatabasePath(TelemetrySettings telemetry, string configDirectory)
        {
            if (!string.IsNullOrWhiteSpace(telemetry.DatabasePath))
            {
                return telemetry.DatabasePath!;
            }

            string directory = string.IsNullOrWhiteSpace(configDirectory)
                ? Directory.GetCurrentDirectory()
                : configDirectory;

            return Path.Combine(directory, "usage.db");
        }

        #endregion
    }
}
