namespace Mux.Server.Models
{
    /// <summary>
    /// One bundled operator tool shown on the dashboard's External Services card (for example Grafana), with
    /// the browser-reachable URL and its local default credentials.
    /// </summary>
    public class ExternalServiceDto
    {
        /// <summary>Stable kind used by the dashboard for its localized description: grafana, prometheus, tempo, loki, or scrape.</summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>Display name (for example Grafana).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The URL a browser on this machine opens.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Local default credentials as display text (for example <c>admin / admin</c>), or null when the tool has no login.</summary>
        public string? Credentials { get; set; }
    }
}
