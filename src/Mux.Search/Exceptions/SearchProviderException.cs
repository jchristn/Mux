namespace Mux.Search.Exceptions
{
    using System;
    using System.Net;

    /// <summary>
    /// Represents a provider-specific search failure.
    /// </summary>
    public class SearchProviderException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchProviderException"/> class.
        /// </summary>
        /// <param name="providerName">The provider name.</param>
        /// <param name="statusCode">Optional HTTP status code.</param>
        /// <param name="responseBody">Optional raw response body.</param>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">Optional inner exception.</param>
        public SearchProviderException(
            string providerName,
            HttpStatusCode? statusCode,
            string? responseBody,
            string message,
            Exception? innerException = null) : base(message, innerException)
        {
            ProviderName = providerName ?? throw new ArgumentNullException(nameof(providerName));
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        /// <summary>
        /// The provider name.
        /// </summary>
        public string ProviderName { get; }

        /// <summary>
        /// Optional HTTP status code.
        /// </summary>
        public HttpStatusCode? StatusCode { get; }

        /// <summary>
        /// Optional raw provider response body.
        /// </summary>
        public string? ResponseBody { get; }
    }
}
