namespace Mux.Search.Exceptions
{
    using System;
    using System.Collections.Generic;
    using Mux.Search.Models;

    /// <summary>
    /// Represents a normalized web-search failure after one or more provider attempts.
    /// </summary>
    public class WebSearchException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WebSearchException"/> class.
        /// </summary>
        /// <param name="message">Failure summary.</param>
        /// <param name="attempts">Provider attempts made.</param>
        /// <param name="innerException">Optional final inner exception.</param>
        public WebSearchException(string message, IReadOnlyList<WebSearchProviderAttempt> attempts, Exception? innerException = null)
            : base(message, innerException)
        {
            Attempts = attempts ?? Array.Empty<WebSearchProviderAttempt>();
        }

        /// <summary>
        /// Provider attempts made during the request.
        /// </summary>
        public IReadOnlyList<WebSearchProviderAttempt> Attempts { get; }
    }
}
