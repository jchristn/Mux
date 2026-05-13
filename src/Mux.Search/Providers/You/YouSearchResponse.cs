namespace Mux.Search.Providers.You
{
    using Mux.Search.Models;

    /// <summary>
    /// Response model for You.com search requests.
    /// </summary>
    public class YouSearchResponse : SearchResponse
    {
        /// <summary>
        /// Provider search identifier.
        /// </summary>
        public string? SearchUuid { get; set; }
    }
}
