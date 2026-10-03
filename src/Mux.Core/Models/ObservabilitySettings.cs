namespace Mux.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Configuration for exporting mux's OpenTelemetry signals (metrics, traces, and logs) to an operator's
    /// observability stack: an OTLP collector, an in-process Prometheus scrape endpoint, and optionally Loki.
    /// This is separate from <see cref="TelemetrySettings"/>, which controls the local SQLite usage history.
    /// <para>
    /// Mux always emits through the .NET <c>Meter</c> and <c>ActivitySource</c> named <c>Mux</c> (free when
    /// nothing listens); these settings only control whether the mux executables (<c>mux</c>, <c>mux serve</c>,
    /// the tray agent, and the desktop app) start an exporter that collects and ships them. Export is off by
    /// default because mux usually runs on a developer machine with no collector; turn it on with
    /// <c>"observability": { "enabled": true }</c> in settings.json or <c>MUX_OBSERVABILITY_ENABLED=true</c>.
    /// All loopback defaults use <c>127.0.0.1</c>. See <c>TELEMETRY.md</c>.
    /// </para>
    /// </summary>
    public class ObservabilitySettings
    {
        #region Private-Members

        private bool _Enabled = false;
        private string _ServiceName = "mux";
        private bool _OtlpEnabled = true;
        private string _OtlpEndpoint = "http://127.0.0.1:4317";
        private string _OtlpProtocol = "grpc";
        private bool _PrometheusEnabled = false;
        private string _PrometheusHostname = "127.0.0.1";
        private int _PrometheusPort = 9464;
        private bool _LogsEnabled = true;
        private bool _LokiEnabled = false;
        private string _LokiEndpoint = "http://127.0.0.1:3100/otlp";
        private double _TraceSamplingRatio = 1.0;
        private int _MetricsExportIntervalMs = 15000;
        private string _GrafanaUrl = "http://127.0.0.1:3000";
        private string _PrometheusUrl = "http://127.0.0.1:9090";
        private string _TempoUrl = "http://127.0.0.1:3200";
        private string _LokiUrl = "http://127.0.0.1:3100";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ObservabilitySettings"/> class with default values.
        /// </summary>
        public ObservabilitySettings()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// Master switch for exporting. Defaults to false. When false no exporter is started, no port is bound,
        /// and no connection is opened; instruments stay inert.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// The <c>service.name</c> resource attribute stamped on every signal. Defaults to <c>mux</c>. The REST
        /// server process appends nothing; set distinct names per machine if several report to one backend.
        /// A blank value resets to the default.
        /// </summary>
        [JsonPropertyName("serviceName")]
        public string ServiceName
        {
            get => _ServiceName;
            set => _ServiceName = string.IsNullOrWhiteSpace(value) ? "mux" : value.Trim();
        }

        /// <summary>
        /// Whether metrics, traces, and logs are pushed over OTLP to <see cref="OtlpEndpoint"/>. Defaults to true
        /// (effective only when <see cref="Enabled"/> is true).
        /// </summary>
        [JsonPropertyName("otlpEnabled")]
        public bool OtlpEnabled
        {
            get => _OtlpEnabled;
            set => _OtlpEnabled = value;
        }

        /// <summary>
        /// The OTLP collector endpoint. Defaults to <c>http://127.0.0.1:4317</c> (gRPC). Use port 4318 with
        /// <see cref="OtlpProtocol"/> <c>httpprotobuf</c>. Must be an absolute http(s) URI; an invalid value
        /// resets to the default.
        /// </summary>
        [JsonPropertyName("otlpEndpoint")]
        public string OtlpEndpoint
        {
            get => _OtlpEndpoint;
            set => _OtlpEndpoint = IsHttpUri(value) ? value.Trim() : "http://127.0.0.1:4317";
        }

        /// <summary>
        /// The OTLP protocol: <c>grpc</c> (default) or <c>httpprotobuf</c>. Any other value resets to <c>grpc</c>.
        /// </summary>
        [JsonPropertyName("otlpProtocol")]
        public string OtlpProtocol
        {
            get => _OtlpProtocol;
            set => _OtlpProtocol = string.Equals(value?.Trim(), "httpprotobuf", StringComparison.OrdinalIgnoreCase) ? "httpprotobuf" : "grpc";
        }

        /// <summary>
        /// Whether a long-running mux process (<c>mux serve</c>, the tray agent, the desktop app) serves an
        /// in-process Prometheus scrape endpoint at <c>http://{PrometheusHostname}:{PrometheusPort}/metrics</c>.
        /// Defaults to false. Short-lived CLI commands never bind it. The endpoint has no authentication; keep
        /// it on loopback or an internal network.
        /// </summary>
        [JsonPropertyName("prometheusEnabled")]
        public bool PrometheusEnabled
        {
            get => _PrometheusEnabled;
            set => _PrometheusEnabled = value;
        }

        /// <summary>
        /// The hostname the Prometheus endpoint binds to. Defaults to <c>127.0.0.1</c>. A blank value resets to
        /// the default.
        /// </summary>
        [JsonPropertyName("prometheusHostname")]
        public string PrometheusHostname
        {
            get => _PrometheusHostname;
            set => _PrometheusHostname = string.IsNullOrWhiteSpace(value) ? "127.0.0.1" : value.Trim();
        }

        /// <summary>
        /// The Prometheus endpoint port. Defaults to 9464. Clamped to 1-65535.
        /// </summary>
        [JsonPropertyName("prometheusPort")]
        public int PrometheusPort
        {
            get => _PrometheusPort;
            set => _PrometheusPort = Math.Clamp(value, 1, 65535);
        }

        /// <summary>
        /// Whether mux's diagnostic log lines (server request log, background-worker diagnostics) are exported
        /// as OpenTelemetry logs, stamped with the active trace and span id. Defaults to true (effective only
        /// when <see cref="Enabled"/> is true).
        /// </summary>
        [JsonPropertyName("logsEnabled")]
        public bool LogsEnabled
        {
            get => _LogsEnabled;
            set => _LogsEnabled = value;
        }

        /// <summary>
        /// Whether logs are also pushed directly to a Loki 3.x OTLP endpoint (bypassing the collector).
        /// Defaults to false; with the bundled compose stack, logs reach Loki through the collector.
        /// </summary>
        [JsonPropertyName("lokiEnabled")]
        public bool LokiEnabled
        {
            get => _LokiEnabled;
            set => _LokiEnabled = value;
        }

        /// <summary>
        /// The Loki OTLP base endpoint used when <see cref="LokiEnabled"/> is true. Defaults to
        /// <c>http://127.0.0.1:3100/otlp</c>. An invalid value resets to the default.
        /// </summary>
        [JsonPropertyName("lokiEndpoint")]
        public string LokiEndpoint
        {
            get => _LokiEndpoint;
            set => _LokiEndpoint = IsHttpUri(value) ? value.Trim() : "http://127.0.0.1:3100/otlp";
        }

        /// <summary>
        /// Head-based trace sampling ratio for root spans. Defaults to 1.0 (sample everything). Clamped to 0.0-1.0;
        /// child spans follow their parent's decision.
        /// </summary>
        [JsonPropertyName("traceSamplingRatio")]
        public double TraceSamplingRatio
        {
            get => _TraceSamplingRatio;
            set => _TraceSamplingRatio = double.IsNaN(value) ? 1.0 : Math.Clamp(value, 0.0, 1.0);
        }

        /// <summary>
        /// How often metrics are pushed over OTLP, in milliseconds. Defaults to 15000. Clamped to 1000-300000.
        /// </summary>
        [JsonPropertyName("metricsExportIntervalMs")]
        public int MetricsExportIntervalMs
        {
            get => _MetricsExportIntervalMs;
            set => _MetricsExportIntervalMs = Math.Clamp(value, 1000, 300000);
        }

        /// <summary>
        /// The browser-reachable Grafana URL shown on the dashboard's External Services card. Defaults to
        /// <c>http://127.0.0.1:3000</c> (the bundled <c>docker/compose.yaml</c>). An invalid value resets to the default.
        /// </summary>
        [JsonPropertyName("grafanaUrl")]
        public string GrafanaUrl
        {
            get => _GrafanaUrl;
            set => _GrafanaUrl = IsHttpUri(value) ? value.Trim() : "http://127.0.0.1:3000";
        }

        /// <summary>
        /// The browser-reachable Prometheus URL shown on the External Services card. Defaults to
        /// <c>http://127.0.0.1:9090</c>. An invalid value resets to the default.
        /// </summary>
        [JsonPropertyName("prometheusUrl")]
        public string PrometheusUrl
        {
            get => _PrometheusUrl;
            set => _PrometheusUrl = IsHttpUri(value) ? value.Trim() : "http://127.0.0.1:9090";
        }

        /// <summary>
        /// The browser-reachable Tempo API URL shown on the External Services card. Defaults to
        /// <c>http://127.0.0.1:3200</c>. An invalid value resets to the default.
        /// </summary>
        [JsonPropertyName("tempoUrl")]
        public string TempoUrl
        {
            get => _TempoUrl;
            set => _TempoUrl = IsHttpUri(value) ? value.Trim() : "http://127.0.0.1:3200";
        }

        /// <summary>
        /// The browser-reachable Loki API URL shown on the External Services card. Defaults to
        /// <c>http://127.0.0.1:3100</c>. An invalid value resets to the default.
        /// </summary>
        [JsonPropertyName("lokiUrl")]
        public string LokiUrl
        {
            get => _LokiUrl;
            set => _LokiUrl = IsHttpUri(value) ? value.Trim() : "http://127.0.0.1:3100";
        }

        #endregion

        #region Private-Methods

        private static bool IsHttpUri(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        #endregion
    }
}
