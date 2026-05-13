namespace Mux.Search.Abstractions
{
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Search.Models;

    /// <summary>
    /// Defines the common contract for a provider-backed search client.
    /// </summary>
    /// <typeparam name="TQuery">The query model accepted by the provider.</typeparam>
    /// <typeparam name="TResponse">The response model returned by the provider.</typeparam>
    public interface ISearchProvider<in TQuery, TResponse>
        where TQuery : SearchQuery
        where TResponse : SearchResponse
    {
        /// <summary>
        /// The provider name.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Executes a search request.
        /// </summary>
        /// <param name="query">The provider-specific query.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The provider-specific response.</returns>
        Task<TResponse> SearchAsync(TQuery query, CancellationToken cancellationToken = default);
    }
}
