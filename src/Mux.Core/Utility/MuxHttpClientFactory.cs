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
            if (!ignoreCertErrors)
            {
                return new HttpClient();
            }

            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            return new HttpClient(handler, disposeHandler: true);
        }
    }
}
