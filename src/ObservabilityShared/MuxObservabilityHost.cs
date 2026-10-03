namespace Mux.Hosting
{
    using System;
    using System.Collections.Generic;
    using Microsoft.Extensions.Logging;
    using Mux.Core.Models;
    using Mux.Core.Observability;
    using Mux.Core.Settings;
    using Radiant;

    /// <summary>
    /// The composition-root telemetry host shared by the mux executables (<c>mux</c>, the tray agent, and the
    /// desktop app; compiled into each by source link). It owns exactly one Radiant host per process,
    /// subscribed to every meter and activity source mux runs (<c>Mux</c>, <c>Watson</c>,
    /// <c>System.Net.Http</c>), exporting over OTLP, optionally serving an in-process Prometheus endpoint, and
    /// shipping logs. It is driven by <see cref="ObservabilitySettings"/>; when export is disabled (the
    /// default) it starts nothing and costs nothing.
    /// <para>
    /// Best-effort by construction: a start failure (for example the Prometheus port already bound by another
    /// mux process) retries without the scrape endpoint, and a second failure leaves the host inert. Nothing
    /// here can stop mux from running. Thread safety: <see cref="Start"/> and <see cref="Dispose"/> are
    /// intended for the process entry point; <see cref="CreateLogSink"/> is safe from any thread.
    /// </para>
    /// </summary>
    public sealed class MuxObservabilityHost : IDisposable
    {
        #region Private-Members

        private static readonly object _CurrentLock = new object();
        private static MuxObservabilityHost? _Current = null;

        private readonly RadiantHost? _Host;
        private readonly string? _PrometheusScrapeUrl;
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// The host started for this process, or null when none was started (or it was disposed).
        /// </summary>
        public static MuxObservabilityHost? Current
        {
            get
            {
                lock (_CurrentLock)
                {
                    return _Current;
                }
            }
        }

        /// <summary>
        /// Whether a live export pipeline is running.
        /// </summary>
        public bool IsEnabled
        {
            get => _Host != null && _Host.IsEnabled;
        }

        /// <summary>
        /// The in-process Prometheus scrape URL when the endpoint is being served; otherwise null.
        /// </summary>
        public string? PrometheusScrapeUrl
        {
            get => _PrometheusScrapeUrl;
        }

        #endregion

        #region Constructors-and-Factories

        private MuxObservabilityHost(RadiantHost? host, string? prometheusScrapeUrl)
        {
            _Host = host;
            _PrometheusScrapeUrl = prometheusScrapeUrl;
        }

        /// <summary>
        /// Loads settings and starts the process host. Never throws; returns an inert host when export is
        /// disabled or startup fails.
        /// </summary>
        /// <param name="longRunning">True for long-lived processes (<c>mux serve</c>, tray agent, desktop) that
        /// may serve the Prometheus endpoint; short CLI commands pass false and only push over OTLP.</param>
        /// <param name="diagnostics">Optional sink for startup diagnostics. Null discards them.</param>
        /// <returns>The started host; never null.</returns>
        public static MuxObservabilityHost StartFromSettings(bool longRunning, Action<string>? diagnostics)
        {
            try
            {
                MuxSettings settings = SettingsLoader.LoadSettings();
                PublishConfiguration(settings);
                return Start(settings.Observability, longRunning, diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics?.Invoke("observability disabled (settings unavailable): " + ex.Message);
                return new MuxObservabilityHost(null, null);
            }
        }

        /// <summary>
        /// Starts the process host from explicit settings. Never throws; returns an inert host when export is
        /// disabled or startup fails.
        /// </summary>
        /// <param name="settings">The observability settings. Null disables export.</param>
        /// <param name="longRunning">Whether the process may serve the Prometheus endpoint.</param>
        /// <param name="diagnostics">Optional sink for startup diagnostics. Null discards them.</param>
        /// <returns>The started host; never null.</returns>
        public static MuxObservabilityHost Start(ObservabilitySettings? settings, bool longRunning, Action<string>? diagnostics)
        {
            if (settings == null || !settings.Enabled)
            {
                return new MuxObservabilityHost(null, null);
            }

            bool servePrometheus = longRunning && settings.PrometheusEnabled;
            RadiantHost? host = TryStartRadiant(settings, servePrometheus, diagnostics, out string? scrapeUrl);
            if (host == null && servePrometheus)
            {
                diagnostics?.Invoke("observability: retrying without the Prometheus endpoint");
                host = TryStartRadiant(settings, false, diagnostics, out scrapeUrl);
            }

            MuxObservabilityHost result = new MuxObservabilityHost(host, scrapeUrl);
            if (host != null)
            {
                lock (_CurrentLock)
                {
                    _Current = result;
                }
            }

            return result;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the Radiant catalog entries for mux's histograms so they get second-scale bucket boundaries
        /// (the SDK defaults target milliseconds). Each convention declares the instrument's real label keys.
        /// </summary>
        /// <returns>The histogram conventions.</returns>
        public static List<Convention> BuildHistogramConventions()
        {
            Dictionary<string, string[]> labels = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                { MuxTelemetryNames.AgentRunDuration, new[] { MuxTelemetryNames.LabelOutcome, MuxTelemetryNames.LabelCallKind } },
                { MuxTelemetryNames.AgentStageDuration, new[] { MuxTelemetryNames.LabelStage, MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.SubagentDuration, new[] { MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.LlmRequestDuration, new[] { MuxTelemetryNames.LabelProvider, MuxTelemetryNames.LabelLlmOperation, MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.LlmTimeToFirstToken, new[] { MuxTelemetryNames.LabelProvider } },
                { MuxTelemetryNames.IntegrationDuration, new[] { MuxTelemetryNames.LabelService, MuxTelemetryNames.LabelOperation, MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.JobStageDuration, new[] { MuxTelemetryNames.LabelStage } },
                { MuxTelemetryNames.WriteLeaseWaitDuration, new[] { MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.ToolDuration, new[] { MuxTelemetryNames.LabelToolKind, MuxTelemetryNames.LabelToolName, MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.UsageWriteDuration, new[] { MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.SessionOperationDuration, new[] { MuxTelemetryNames.LabelOperation, MuxTelemetryNames.LabelOutcome } },
                { MuxTelemetryNames.CheckpointDuration, new[] { MuxTelemetryNames.LabelOperation, MuxTelemetryNames.LabelOutcome } }
            };

            List<Convention> conventions = new List<Convention>();
            foreach (string name in MuxTelemetry.LongHistogramNames)
            {
                conventions.Add(Convention.Histogram(name, "s", MuxTelemetry.LongBuckets, LabelsFor(labels, name)));
            }

            foreach (string name in MuxTelemetry.ShortHistogramNames)
            {
                conventions.Add(Convention.Histogram(name, "s", MuxTelemetry.ShortBuckets, LabelsFor(labels, name)));
            }

            return conventions;
        }

        /// <summary>
        /// Creates a log sink for mux's <c>Action&lt;string&gt;</c> diagnostic callbacks that forwards each line to
        /// the export pipeline as an OpenTelemetry log record (stamped with the active trace and span id).
        /// </summary>
        /// <param name="category">The logger category (for example <c>Mux.Server</c>).</param>
        /// <returns>A sink, or null when export is disabled (callers then keep their existing behavior).</returns>
        public Action<string>? CreateLogSink(string category)
        {
            if (_Host == null || !_Host.IsEnabled) return null;

            try
            {
                ILogger logger = _Host.CreateLogger(string.IsNullOrWhiteSpace(category) ? "Mux" : category);
                return (string message) =>
                {
                    try
                    {
                        logger.LogInformation("{Message}", message);
                    }
                    catch (Exception)
                    {
                        // Best-effort: a failed log export never affects the caller.
                    }
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Flushes and tears down the export pipeline and releases the Prometheus port. Never throws.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            lock (_CurrentLock)
            {
                if (ReferenceEquals(_Current, this)) _Current = null;
            }

            try
            {
                _Host?.Dispose();
            }
            catch (Exception)
            {
                // Best-effort shutdown.
            }
        }

        #endregion

        #region Private-Methods

        private static RadiantHost? TryStartRadiant(ObservabilitySettings settings, bool servePrometheus, Action<string>? diagnostics, out string? scrapeUrl)
        {
            scrapeUrl = null;
            try
            {
                RadiantSettings radiant = new RadiantSettings(settings.ServiceName);
                radiant.Enable = true;
                radiant.DiagnosticCallback = diagnostics;

                radiant.Sources.AddMeter(MuxTelemetryNames.MeterName);
                radiant.Sources.AddActivitySource(MuxTelemetryNames.ActivitySourceName);
                radiant.Sources.AddMeter("Watson");
                radiant.Sources.AddActivitySource("Watson");
                radiant.Sources.AddMeter("System.Net.Http");
                radiant.Sources.AddActivitySource("System.Net.Http");

                radiant.Otlp.Enable = settings.OtlpEnabled;
                radiant.Otlp.Endpoint = settings.OtlpEndpoint;
                radiant.Otlp.Protocol = string.Equals(settings.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                    ? OtlpProtocolEnum.HttpProtobuf
                    : OtlpProtocolEnum.Grpc;

                radiant.Metrics.ExportIntervalMs = settings.MetricsExportIntervalMs;
                radiant.Metrics.IncludeRuntime = true;
                radiant.Metrics.IncludeProcess = true;
                radiant.Metrics.DefineAll(BuildHistogramConventions());

                radiant.Traces.SamplingRatio = settings.TraceSamplingRatio;
                radiant.Traces.PropagateContext = true;

                radiant.Logs.Enable = settings.LogsEnabled;
                radiant.Loki.Enable = settings.LokiEnabled;
                radiant.Loki.Endpoint = settings.LokiEndpoint;

                radiant.Prometheus.Enable = servePrometheus;
                radiant.Prometheus.Hostname = settings.PrometheusHostname;
                radiant.Prometheus.Port = settings.PrometheusPort;

                RadiantHost host = RadiantHost.Start(radiant);
                if (servePrometheus) scrapeUrl = radiant.Prometheus.ToScrapeUrl();
                diagnostics?.Invoke("observability: exporting as '" + settings.ServiceName + "'"
                    + (settings.OtlpEnabled ? " via OTLP " + settings.OtlpEndpoint : string.Empty)
                    + (scrapeUrl != null ? ", Prometheus at " + scrapeUrl : string.Empty));
                return host;
            }
            catch (Exception ex)
            {
                diagnostics?.Invoke("observability: export host failed to start: " + ex.Message);
                return null;
            }
        }

        private static void PublishConfiguration(MuxSettings settings)
        {
            int endpointCount = -1;
            int mcpCount = -1;
            try { endpointCount = SettingsLoader.LoadEndpoints().Count; } catch (Exception) { }
            try { mcpCount = SettingsLoader.LoadMcpServers().Count; } catch (Exception) { }
            MuxTelemetry.SetConfiguration(endpointCount, mcpCount, settings.MaxConcurrency);
        }

        private static string[] LabelsFor(Dictionary<string, string[]> labels, string name)
        {
            return labels.TryGetValue(name, out string[]? keys) ? keys : Array.Empty<string>();
        }

        #endregion
    }
}
