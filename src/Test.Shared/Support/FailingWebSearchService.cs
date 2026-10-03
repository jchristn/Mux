namespace Test.Shared.Support
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Models;
    using Mux.Search.Services;

    /// <summary>
    /// A web-search service that always throws, used to exercise the web-search failure path.
    /// </summary>
    public sealed class FailingWebSearchService : IWebSearchService
    {
        /// <summary>
        /// Always throws <see cref="InvalidOperationException"/>.
        /// </summary>
        /// <param name="request">The request (ignored).</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="InvalidOperationException">Always.</exception>
        public Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("search provider unavailable (test)");
        }
    }
}
