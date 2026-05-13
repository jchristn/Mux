namespace Mux.Search.Services
{
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Models;

    /// <summary>
    /// Mux-facing normalized web-search contract independent of provider-specific query types.
    /// </summary>
    public interface IWebSearchService
    {
        /// <summary>
        /// Executes a normalized web-search request.
        /// </summary>
        /// <param name="request">The request to execute.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The normalized response.</returns>
        Task<WebSearchResponse> SearchAsync(WebSearchRequest request, CancellationToken cancellationToken = default);
    }
}
