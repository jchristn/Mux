namespace Test.Shared.Search
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Test <see cref="HttpMessageHandler"/> that delegates each request to a caller-supplied responder
    /// function, allowing per-request assertions and canned responses in web-search tests.
    /// </summary>
    public sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _Responder;

        /// <summary>
        /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="responder">The function that produces a response for each request. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="responder"/> is null.</exception>
        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _Responder = responder ?? throw new ArgumentNullException(nameof(responder));
        }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_Responder(request));
        }
    }
}
