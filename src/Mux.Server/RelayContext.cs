namespace Mux.Server
{
    using System;
    using MuxCloud.Protocol;
    using WatsonWebserver.Core;

    /// <summary>
    /// Reads the headers the Mux Cloud tunnel forwarder adds to requests it relays from the cloud to this
    /// machine's local API (<see cref="TunnelProtocol.RelayHeader"/> and <see cref="TunnelProtocol.BasePathHeader"/>).
    /// A local caller can set them too; doing so only changes how the dashboard page builds its URLs, never what
    /// the caller may do.
    /// <para>Thread safety: stateless; safe to call concurrently.</para>
    /// </summary>
    public static class RelayContext
    {
        #region Public-Methods

        /// <summary>
        /// Whether the request arrived through the Mux Cloud relay.
        /// </summary>
        /// <param name="ctx">The request context. Null returns false.</param>
        /// <returns>True when the relay header carries <see cref="TunnelProtocol.RelayHeaderValue"/>.</returns>
        public static bool IsRelayed(HttpContextBase? ctx)
        {
            string? value = ctx?.Request?.Headers?[TunnelProtocol.RelayHeader];
            return string.Equals(value?.Trim(), TunnelProtocol.RelayHeaderValue, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The sanitized relay base path of the request, or empty.
        /// </summary>
        /// <param name="ctx">The request context. Null returns empty.</param>
        /// <returns>The base path, for example <c>/m/mch_abc</c>.</returns>
        public static string GetBasePath(HttpContextBase? ctx)
        {
            return SanitizeBasePath(ctx?.Request?.Headers?[TunnelProtocol.BasePathHeader]);
        }

        /// <summary>
        /// Accept a base path only when it is an absolute path made of letters, digits, <c>_</c>, <c>-</c>, and
        /// <c>/</c>, with no trailing slash and at most 128 characters. Anything else returns empty, so a hostile
        /// header can never inject script into the rendered page.
        /// </summary>
        /// <param name="value">Candidate path; null returns empty.</param>
        /// <returns>The path, or empty.</returns>
        public static string SanitizeBasePath(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string path = value.Trim().TrimEnd('/');
            if (path.Length == 0 || path.Length > 128 || path[0] != '/') return string.Empty;
            foreach (char c in path)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '/';
                if (!ok) return string.Empty;
            }

            return path.Contains("//", StringComparison.Ordinal) ? string.Empty : path;
        }

        #endregion
    }
}
