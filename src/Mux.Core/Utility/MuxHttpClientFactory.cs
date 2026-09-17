namespace Mux.Core.Utility
{
    using System.Net.Http;

    /// <summary>
    /// Creates HTTP clients with mux-wide transport options applied.
    /// </summary>
    public static class MuxHttpClientFactory
    {
        /// <summary>
        /// Creates a new <see cref="HttpClient"/> for mux-owned outbound requests.
        /// </summary>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation.</param>
        /// <returns>A configured <see cref="HttpClient"/> instance.</returns>
        public static HttpClient Create(bool ignoreCertErrors)
        {
            return Create(ignoreCertErrors, null);
        }

        /// <summary>
        /// Creates a new <see cref="HttpClient"/> for mux-owned outbound requests, optionally routing every
        /// request through the supplied <paramref name="authHandler"/> (for example, to append a query-string
        /// credential). When supplied, its <see cref="DelegatingHandler.InnerHandler"/> is set to the transport
        /// handler chosen by <paramref name="ignoreCertErrors"/>, and the client owns the whole chain.
        /// </summary>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation.</param>
        /// <param name="authHandler">An optional delegating handler placed in front of the transport.</param>
        /// <returns>A configured <see cref="HttpClient"/> instance.</returns>
        public static HttpClient Create(bool ignoreCertErrors, DelegatingHandler? authHandler)
        {
            HttpClientHandler transport = new HttpClientHandler();
            if (ignoreCertErrors)
            {
                transport.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            if (authHandler == null)
            {
                return new HttpClient(transport, disposeHandler: true);
            }

            authHandler.InnerHandler = transport;
            return new HttpClient(authHandler, disposeHandler: true);
        }
    }
}
