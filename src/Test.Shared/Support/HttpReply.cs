namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A captured HTTP exchange for route tests: the request method and URL, the status code and reason phrase, the
    /// content type and length, every response and content header (case-insensitive; sensitive values such as
    /// <c>Set-Cookie</c> and <c>Authorization</c> redacted), the body (cut at <see cref="MaxBodyChars"/>) with a
    /// truncation flag, and the elapsed time.
    /// </summary>
    public sealed class HttpReply
    {
        #region Private-Members

        private static readonly HashSet<string> _Sensitive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Set-Cookie", "Set-Cookie2", "Cookie", "Authorization", "Proxy-Authorization", "X-Api-Key", "X-Auth-Token"
        };

        #endregion

        #region Constructors-and-Factories

        private HttpReply()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>The longest body kept, in characters.</summary>
        public const int MaxBodyChars = 65536;

        /// <summary>The value that replaces sensitive header values.</summary>
        public const string Redacted = "<redacted>";

        /// <summary>The request method.</summary>
        public string Method { get; private set; } = string.Empty;

        /// <summary>The request URL.</summary>
        public string Url { get; private set; } = string.Empty;

        /// <summary>The HTTP status code.</summary>
        public int StatusCode { get; private set; }

        /// <summary>The reason phrase, for example <c>OK</c> or <c>Not Found</c>.</summary>
        public string ReasonPhrase { get; private set; } = string.Empty;

        /// <summary>The response content type, or empty.</summary>
        public string ContentType { get; private set; } = string.Empty;

        /// <summary>The declared content length, or null.</summary>
        public long? ContentLength { get; private set; }

        /// <summary>Response and content headers by name (case-insensitive), sensitive values redacted.</summary>
        public Dictionary<string, string> Headers { get; private set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The body text, cut at <see cref="MaxBodyChars"/>.</summary>
        public string Body { get; private set; } = string.Empty;

        /// <summary>Whether <see cref="Body"/> was cut.</summary>
        public bool BodyTruncated { get; private set; }

        /// <summary>Milliseconds from sending the request to reading the whole reply.</summary>
        public long ElapsedMs { get; private set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sends a request and captures the full reply.
        /// </summary>
        /// <param name="http">The client.</param>
        /// <param name="request">The request (disposed by the caller).</param>
        /// <param name="cancellationToken">Cancels the exchange.</param>
        /// <returns>The captured reply.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<HttpReply> SendAsync(HttpClient http, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (http == null) throw new ArgumentNullException(nameof(http));
            if (request == null) throw new ArgumentNullException(nameof(request));

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            using (HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false))
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                HttpReply reply = new HttpReply
                {
                    Method = request.Method.Method,
                    Url = request.RequestUri?.ToString() ?? string.Empty,
                    StatusCode = (int)response.StatusCode,
                    ReasonPhrase = response.ReasonPhrase ?? string.Empty,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                    ContentLength = response.Content.Headers.ContentLength,
                    BodyTruncated = body.Length > MaxBodyChars,
                    Body = body.Length > MaxBodyChars ? body.Substring(0, MaxBodyChars) : body,
                    ElapsedMs = watch.ElapsedMilliseconds
                };

                foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
                {
                    reply.Headers[header.Key] = _Sensitive.Contains(header.Key) ? Redacted : string.Join(", ", header.Value);
                }

                return reply;
            }
        }

        /// <summary>
        /// Returns a header value, or null when absent.
        /// </summary>
        /// <param name="name">The header name (case-insensitive).</param>
        /// <returns>The value, or null.</returns>
        public string? GetHeader(string name)
        {
            return Headers.TryGetValue(name, out string? value) ? value : null;
        }

        #endregion
    }
}
