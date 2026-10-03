namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// OpenTelemetry export status and the bundled observability tools, for the dashboard's External Services
    /// card. Derived from the <c>observability</c> settings block; contains no secrets.
    /// </summary>
    public class OverviewObservabilityDto
    {
        /// <summary>Whether OpenTelemetry export is enabled in settings.</summary>
        public bool Enabled { get; set; }

        /// <summary>The <c>service.name</c> mux reports as.</summary>
        public string ServiceName { get; set; } = "mux";

        /// <summary>The OTLP collector endpoint when OTLP push is enabled; otherwise null.</summary>
        public string? OtlpEndpoint { get; set; }

        /// <summary>The bundled tools (Grafana, Prometheus, Tempo, Loki, and this process's scrape endpoint when served).</summary>
        public List<ExternalServiceDto> Services { get; set; } = new List<ExternalServiceDto>();
    }
}
