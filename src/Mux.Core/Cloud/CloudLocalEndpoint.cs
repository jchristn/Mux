namespace Mux.Core.Cloud
{
    using System;

    /// <summary>
    /// Where the tunnel forwards relayed traffic on this machine: the local hub's loopback URL, its API key, and the
    /// machine identifier used to build the relay base path.
    /// </summary>
    public class CloudLocalEndpoint
    {
        #region Private-Members

        private string _BaseUrl = "http://127.0.0.1:8710";

        #endregion

        #region Public-Members

        /// <summary>Loopback base URL of the local hub, for example <c>http://127.0.0.1:8710</c>.</summary>
        public string BaseUrl
        {
            get => _BaseUrl;
            set => _BaseUrl = string.IsNullOrWhiteSpace(value) ? "http://127.0.0.1:8710" : value.Trim().TrimEnd('/');
        }

        /// <summary>Local API key added to every forwarded request, or null in no-auth mode.</summary>
        public string? ApiKey { get; set; }

        /// <summary>Enrolled machine identifier, used for the <c>X-Mux-Base-Path</c> header (<c>/m/{machineId}</c>).</summary>
        public string MachineId { get; set; } = string.Empty;

        /// <summary>Base path the relay serves this machine under.</summary>
        public string BasePath => string.IsNullOrEmpty(MachineId) ? string.Empty : MuxCloud.Protocol.TunnelProtocol.MachinePathPrefix + MachineId;

        /// <summary>Build the loopback WebSocket URI for a path and query.</summary>
        /// <param name="path">Absolute path, already URL-encoded as received.</param>
        /// <param name="query">Query without the question mark, or empty.</param>
        /// <returns>URI.</returns>
        public Uri WebSocketUri(string path, string query)
        {
            string http = Build(path, query);
            return new Uri(http.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? "wss:" + http.Substring(6) : "ws:" + http.Substring(5));
        }

        /// <summary>Build the loopback HTTP URI for a path and query.</summary>
        /// <param name="path">Absolute path, already URL-encoded as received.</param>
        /// <param name="query">Query without the question mark, or empty.</param>
        /// <returns>URI.</returns>
        public Uri HttpUri(string path, string query)
        {
            return new Uri(Build(path, query));
        }

        private string Build(string path, string query)
        {
            string p = string.IsNullOrEmpty(path) || path[0] != '/' ? "/" + (path ?? string.Empty) : path;
            return BaseUrl + p + (string.IsNullOrEmpty(query) ? string.Empty : "?" + query.TrimStart('?'));
        }

        #endregion
    }
}
