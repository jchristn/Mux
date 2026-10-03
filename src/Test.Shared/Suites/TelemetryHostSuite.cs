namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Observability;
    using Mux.Core.Sessions;
    using Mux.Hosting;
    using Mux.Server;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Proves the service-level telemetry wiring: the REST server's Watson telemetry (server span per request,
    /// inbound <c>traceparent</c> adoption, HTTP metrics), the Radiant-backed composition-root host
    /// (<see cref="MuxObservabilityHost"/>: disabled by default, Prometheus scrape with second-scale histogram
    /// buckets, port-conflict fallback), and the <see cref="ObservabilitySettings"/> defaults and clamps.
    /// </summary>
    public static class TelemetryHostSuite
    {
        private const string Suite = "TelemetryHost";

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for host telemetry cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                Suite,
                "REST server, export host, and observability settings telemetry wiring",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(Suite, "SettingsDefaultsAndClamps", "Observability export is off by default with 127.0.0.1 defaults and clamped values", SettingsDefaultsAndClampsAsync),
                    new TestCaseDescriptor(Suite, "DisabledHostIsInert", "A disabled export host starts nothing and never throws", DisabledHostIsInertAsync),
                    new TestCaseDescriptor(Suite, "WatsonServerSpansAndMetrics", "The REST server emits a Watson server span that adopts the inbound traceparent", WatsonServerSpansAndMetricsAsync),
                    new TestCaseDescriptor(Suite, "PrometheusScrape", "The export host serves Mux metrics with second-scale buckets and survives a port conflict", PrometheusScrapeAsync),
                    new TestCaseDescriptor(Suite, "RequestLogCarriesTraceContext", "The server's request log line is emitted inside the request's trace so exported logs correlate", RequestLogCarriesTraceContextAsync),
                    new TestCaseDescriptor(Suite, "OverviewReportsObservability", "The overview API reports export status and the External Services tool list", OverviewReportsObservabilityAsync),
                    new TestCaseDescriptor(Suite, "DashboardExternalServicesCard", "The dashboard home page carries the External Services card in every locale", DashboardExternalServicesCardAsync)
                });
        }

        private static Task SettingsDefaultsAndClampsAsync(CancellationToken ct)
        {
            ObservabilitySettings settings = new ObservabilitySettings();
            MuxAssert.IsFalse(settings.Enabled, "export off by default");
            MuxAssert.AreEqual("mux", settings.ServiceName, "service name");
            MuxAssert.AreEqual("http://127.0.0.1:4317", settings.OtlpEndpoint, "otlp endpoint uses 127.0.0.1");
            MuxAssert.AreEqual("grpc", settings.OtlpProtocol, "otlp protocol");
            MuxAssert.IsFalse(settings.PrometheusEnabled, "prometheus off by default");
            MuxAssert.AreEqual("127.0.0.1", settings.PrometheusHostname, "prometheus host");
            MuxAssert.AreEqual(9464, settings.PrometheusPort, "prometheus port");

            settings.PrometheusPort = 0;
            MuxAssert.AreEqual(1, settings.PrometheusPort, "port clamped low");
            settings.PrometheusPort = 70000;
            MuxAssert.AreEqual(65535, settings.PrometheusPort, "port clamped high");
            settings.OtlpProtocol = "HttpProtobuf";
            MuxAssert.AreEqual("httpprotobuf", settings.OtlpProtocol, "protocol normalized");
            settings.OtlpProtocol = "carrier-pigeon";
            MuxAssert.AreEqual("grpc", settings.OtlpProtocol, "invalid protocol reset");
            settings.OtlpEndpoint = "not a uri";
            MuxAssert.AreEqual("http://127.0.0.1:4317", settings.OtlpEndpoint, "invalid endpoint reset");
            settings.TraceSamplingRatio = 5;
            MuxAssert.AreEqual(1.0, settings.TraceSamplingRatio, "sampling clamped");
            settings.MetricsExportIntervalMs = 10;
            MuxAssert.AreEqual(1000, settings.MetricsExportIntervalMs, "interval clamped");
            settings.ServiceName = "  ";
            MuxAssert.AreEqual("mux", settings.ServiceName, "blank service name reset");

            MuxSettings mux = JsonSerializer.Deserialize<MuxSettings>("{\"observability\":{\"enabled\":true,\"prometheusPort\":9999}}")!;
            MuxAssert.IsTrue(mux.Observability.Enabled, "observability block deserializes");
            MuxAssert.AreEqual(9999, mux.Observability.PrometheusPort, "port deserializes");
            MuxAssert.IsNotNull(new MuxSettings().Observability, "default block present");
            ObservabilitySettings urls = new ObservabilitySettings();
            MuxAssert.AreEqual("http://127.0.0.1:3000", urls.GrafanaUrl, "grafana url default");
            MuxAssert.AreEqual("http://127.0.0.1:9090", urls.PrometheusUrl, "prometheus url default");
            MuxAssert.AreEqual("http://127.0.0.1:3200", urls.TempoUrl, "tempo url default");
            MuxAssert.AreEqual("http://127.0.0.1:3100", urls.LokiUrl, "loki url default");
            urls.GrafanaUrl = "javascript:alert(1)";
            MuxAssert.AreEqual("http://127.0.0.1:3000", urls.GrafanaUrl, "non-http card url rejected");
            return Task.CompletedTask;
        }

        private static Task DisabledHostIsInertAsync(CancellationToken ct)
        {
            using (MuxObservabilityHost host = MuxObservabilityHost.Start(new ObservabilitySettings(), true, null))
            {
                MuxAssert.IsFalse(host.IsEnabled, "disabled host inert");
                MuxAssert.IsNull(host.PrometheusScrapeUrl, "no scrape endpoint");
                MuxAssert.IsNull(host.CreateLogSink("Mux.Test"), "no log sink when disabled");
            }

            using (MuxObservabilityHost nullHost = MuxObservabilityHost.Start(null, false, null))
            {
                MuxAssert.IsFalse(nullHost.IsEnabled, "null settings inert");
            }

            MuxAssert.IsTrue(MuxObservabilityHost.BuildHistogramConventions().Count >= MuxTelemetry.LongHistogramNames.Count, "histogram catalog covers mux histograms");
            return Task.CompletedTask;
        }

        private static async Task WatsonServerSpansAndMetricsAsync(CancellationToken ct)
        {
            string sessions = Path.Combine(Path.GetTempPath(), "mux_otel_srv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessions);
            using TelemetryCapture capture = new TelemetryCapture();
            MuxServer? server = null;
            int port = 0;

            try
            {
                RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1", ApiKey = "otelkey" };
                for (int attempt = 0; attempt < 10 && server == null; attempt++)
                {
                    port = FreeLoopbackPort();
                    rest.Port = port;
                    MuxServer candidate = new MuxServer(rest, "9.9.9-otel", new SessionStore(sessions), () => new List<EndpointConfig>(), null);
                    try
                    {
                        candidate.Start();
                        server = candidate;
                    }
                    catch (Exception)
                    {
                        candidate.Dispose();
                        Thread.Sleep(50);
                    }
                }

                MuxAssert.IsNotNull(server, "server started");

                ActivityTraceId upstreamTrace = ActivityTraceId.CreateRandom();
                ActivitySpanId upstreamSpan = ActivitySpanId.CreateRandom();
                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/v1.0/api/health");
                request.Headers.TryAddWithoutValidation("traceparent", "00-" + upstreamTrace.ToHexString() + "-" + upstreamSpan.ToHexString() + "-01");
                using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);
                MuxAssert.AreEqual(HttpStatusCode.OK, response.StatusCode, "health ok");

                bool seen = await TelemetryCapture.WaitForAsync(() => capture.SpansInTrace(upstreamTrace).Any((Activity a) => a.Source.Name == "Watson"), 5000, ct).ConfigureAwait(false);
                MuxAssert.IsTrue(seen, "Watson server span joined the upstream trace");
                Activity serverSpan = capture.SpansInTrace(upstreamTrace).First((Activity a) => a.Source.Name == "Watson");
                MuxAssert.AreEqual(ActivityKind.Server, serverSpan.Kind, "server span kind");
                MuxAssert.AreEqual(upstreamSpan, serverSpan.ParentSpanId, "server span parented to the inbound traceparent");
                MuxAssert.IsTrue(
                    await TelemetryCapture.WaitForAsync(() => capture.Any("http.server.request.duration"), 5000, ct).ConfigureAwait(false),
                    "Watson HTTP duration metric emitted");
            }
            finally
            {
                server?.Dispose();
                try { Directory.Delete(sessions, true); } catch (Exception) { }
            }
        }

        private static async Task PrometheusScrapeAsync(CancellationToken ct)
        {
            int port = FreeLoopbackPort();
            ObservabilitySettings settings = new ObservabilitySettings
            {
                Enabled = true,
                ServiceName = "mux-test",
                OtlpEnabled = false,
                LogsEnabled = false,
                PrometheusEnabled = true,
                PrometheusPort = port
            };

            List<string> diagnostics = new List<string>();
            using (MuxObservabilityHost host = MuxObservabilityHost.Start(settings, true, (string m) => { lock (diagnostics) diagnostics.Add(m); }))
            {
                MuxAssert.IsTrue(host.IsEnabled, "export host enabled");
                MuxAssert.IsNotNull(host.PrometheusScrapeUrl, "scrape url");
                MuxAssert.IsTrue(ReferenceEquals(host, MuxObservabilityHost.Current), "registered as the process host");
                MuxAssert.IsNotNull(host.CreateLogSink("Mux.Test"), "log sink available");
                host.CreateLogSink("Mux.Test")!.Invoke("telemetry host test log line");

                MuxTelemetry.AgentRunStarted();
                MuxTelemetry.RecordAgentRun("completed", "primary", 42.0, 3);
                MuxTelemetry.RecordIntegration(MuxTelemetryNames.ServiceMcp, "tools/call", "success", 0.2);

                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                string body = string.Empty;
                bool found = await TelemetryCapture.WaitForAsync(() =>
                {
                    try
                    {
                        body = http.GetStringAsync(host.PrometheusScrapeUrl, ct).GetAwaiter().GetResult();
                        return body.Contains("mux_agent_run_duration_seconds_bucket");
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }, 10000, ct).ConfigureAwait(false);

                MuxAssert.IsTrue(found, "scrape exposes mux histogram");
                MuxAssert.Contains("mux_agent_runs_total", body, "counter exported with _total");
                MuxAssert.Contains("le=\"1800\"", body, "second-scale bucket boundaries applied");
                MuxAssert.Contains("call_kind=\"primary\"", body, "labels preserved through the catalog");
                MuxAssert.Contains("mux_integration_requests_total", body, "integration counter exported");
                MuxAssert.Contains("mux_build_info", body, "build info exported");

                // A second host on the same port falls back to OTLP-only rather than failing.
                using (MuxObservabilityHost second = MuxObservabilityHost.Start(settings, true, null))
                {
                    MuxAssert.IsNull(second.PrometheusScrapeUrl, "conflicting host gave up the scrape endpoint");
                }
            }

            MuxAssert.IsNull(MuxObservabilityHost.Current, "disposed host deregistered");
        }

        private static async Task RequestLogCarriesTraceContextAsync(CancellationToken ct)
        {
            string sessions = Path.Combine(Path.GetTempPath(), "mux_otel_log_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sessions);
            using TelemetryCapture capture = new TelemetryCapture();
            List<string> correlated = new List<string>();
            Action<string> logger = (string message) =>
            {
                if (!message.Contains("/v1.0/api/health")) return;
                lock (correlated) correlated.Add((Activity.Current?.TraceId.ToHexString() ?? "none") + " " + message);
            };

            MuxServer? server = StartServer(sessions, logger, out int port);
            try
            {
                ActivityTraceId upstreamTrace = ActivityTraceId.CreateRandom();
                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:" + port + "/v1.0/api/health?token=secret-value");
                request.Headers.TryAddWithoutValidation("traceparent", "00-" + upstreamTrace.ToHexString() + "-" + ActivitySpanId.CreateRandom().ToHexString() + "-01");
                using HttpResponseMessage response = await http.SendAsync(request, ct).ConfigureAwait(false);

                MuxAssert.IsTrue(await TelemetryCapture.WaitForAsync(() => { lock (correlated) return correlated.Count > 0; }, 5000, ct).ConfigureAwait(false), "request logged");
                string line;
                lock (correlated) line = correlated[0];
                MuxAssert.Contains(upstreamTrace.ToHexString(), line, "log line emitted inside the request's trace");
                MuxAssert.DoesNotContain("secret-value", line, "query string is not logged");
            }
            finally
            {
                server?.Dispose();
                try { Directory.Delete(sessions, true); } catch (Exception) { }
            }
        }

        private static async Task OverviewReportsObservabilityAsync(CancellationToken ct)
        {
            // The mapping, from explicit settings.
            ObservabilitySettings settings = new ObservabilitySettings
            {
                Enabled = true,
                ServiceName = "mux-card",
                PrometheusEnabled = true,
                PrometheusPort = 9555,
                GrafanaUrl = "http://127.0.0.1:43000"
            };
            Mux.Server.Models.OverviewObservabilityDto mapped = Mux.Server.Routes.OverviewRoutes.BuildObservability(settings);
            MuxAssert.IsTrue(mapped.Enabled, "export enabled reported");
            MuxAssert.AreEqual("mux-card", mapped.ServiceName, "service name reported");
            MuxAssert.AreEqual("http://127.0.0.1:4317", mapped.OtlpEndpoint, "otlp endpoint reported");
            MuxAssert.IsTrue(mapped.Services.Any((Mux.Server.Models.ExternalServiceDto e) => e.Kind == "grafana" && e.Url == "http://127.0.0.1:43000" && e.Credentials == "admin / admin"), "grafana entry uses configured url and shows default credentials");
            MuxAssert.IsTrue(mapped.Services.Any((Mux.Server.Models.ExternalServiceDto e) => e.Kind == "prometheus" && e.Credentials == null), "prometheus entry without login");
            MuxAssert.IsTrue(mapped.Services.Any((Mux.Server.Models.ExternalServiceDto e) => e.Kind == "tempo"), "tempo entry");
            MuxAssert.IsTrue(mapped.Services.Any((Mux.Server.Models.ExternalServiceDto e) => e.Kind == "loki"), "loki entry");
            MuxAssert.IsTrue(mapped.Services.Any((Mux.Server.Models.ExternalServiceDto e) => e.Kind == "scrape" && e.Url == "http://127.0.0.1:9555/metrics"), "scrape entry");

            Mux.Server.Models.OverviewObservabilityDto off = Mux.Server.Routes.OverviewRoutes.BuildObservability(new ObservabilitySettings());
            MuxAssert.IsFalse(off.Enabled, "export off by default");
            MuxAssert.IsNull(off.OtlpEndpoint, "no endpoint shown while off");
            MuxAssert.AreEqual(4, off.Services.Count, "tools still listed while export is off (graceful degradation)");
            MuxAssert.IsFalse(Mux.Server.Routes.OverviewRoutes.BuildObservability(null).Enabled, "null settings safe");

            // The live route carries the block.
            string sessions = Path.Combine(Path.GetTempPath(), "mux_otel_ov_" + Guid.NewGuid().ToString("N"));
            MuxServer? server = StartServer(sessions, null, out int port);
            try
            {
                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string json = await http.GetStringAsync("http://127.0.0.1:" + port + "/v1.0/api/overview", ct).ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);
                JsonElement ob = doc.RootElement.GetProperty("Observability");
                MuxAssert.IsTrue(ob.TryGetProperty("Enabled", out _), "Enabled field present");
                MuxAssert.IsTrue(ob.GetProperty("Services").GetArrayLength() >= 4, "tool list present");
            }
            finally
            {
                server?.Dispose();
                try { Directory.Delete(sessions, true); } catch (Exception) { }
            }
        }

        private static Task DashboardExternalServicesCardAsync(CancellationToken ct)
        {
            string html = DashboardPage.Render(null, "9.9.9-test");
            MuxAssert.Contains("id=\"home_ext_card\"", html, "card present on the home view");
            MuxAssert.Contains("function renderExternalServices(", html, "card renderer defined");
            MuxAssert.Contains("renderExternalServices(d.Observability)", html, "card rendered from the overview payload");
            MuxAssert.Contains("[data-copytext]", html, "copy control wired");
            int locales = html.Split("\"ext.title\":").Length - 1;
            MuxAssert.AreEqual(11, locales, "card title translated in all 11 locales");
            MuxAssert.AreEqual(11, html.Split("\"ext.off\":").Length - 1, "export-off guidance translated in all 11 locales");
            int quick = html.IndexOf("id=\"home_quick\"", StringComparison.Ordinal);
            int card = html.IndexOf("id=\"home_ext_card\"", StringComparison.Ordinal);
            MuxAssert.IsTrue(card > quick, "card sits low on the home page, below quick actions");
            return Task.CompletedTask;
        }

        private static MuxServer? StartServer(string sessionsDir, Action<string>? logger, out int port)
        {
            Directory.CreateDirectory(sessionsDir);
            RestServerSettings rest = new RestServerSettings { Hostname = "127.0.0.1" };
            port = 0;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                port = FreeLoopbackPort();
                rest.Port = port;
                MuxServer candidate = new MuxServer(rest, "9.9.9-otel", new SessionStore(sessionsDir), () => new List<EndpointConfig>(), logger);
                try
                {
                    candidate.Start();
                    return candidate;
                }
                catch (Exception)
                {
                    candidate.Dispose();
                    Thread.Sleep(50);
                }
            }

            throw new AssertionFailedException("server failed to bind a loopback port");
        }

        private static int FreeLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
