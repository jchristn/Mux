namespace Mux.Core.Utility
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Web;

    /// <summary>
    /// A delegating handler that appends an API key as a query-string parameter to every outbound request URI.
    /// Used for services that authenticate with a query-string value rather than a bearer token or header. The
    /// parameter is only added when absent, so an explicit value on the request URI is never overwritten.
    /// </summary>
    public class QueryStringAuthHandler : DelegatingHandler
    {
        #region Private-Members

        private readonly string _ParameterName;
        private readonly string _Value;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryStringAuthHandler"/> class.
        /// </summary>
        /// <param name="parameterName">The query-string parameter name that carries the key.</param>
        /// <param name="value">The API key value to send.</param>
        public QueryStringAuthHandler(string parameterName, string value)
        {
            if (string.IsNullOrWhiteSpace(parameterName)) throw new ArgumentNullException(nameof(parameterName));
            _ParameterName = parameterName;
            _Value = value ?? string.Empty;
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Appends the configured query-string parameter (when absent) before forwarding the request.
        /// </summary>
        /// <param name="request">The outbound request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The upstream response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null)
            {
                UriBuilder builder = new UriBuilder(request.RequestUri);
                System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(builder.Query);
                if (string.IsNullOrEmpty(query.Get(_ParameterName)))
                {
                    query[_ParameterName] = _Value;
                    builder.Query = query.ToString();
                    request.RequestUri = builder.Uri;
                }
            }

            return base.SendAsync(request, cancellationToken);
        }

        #endregion
    }
}
