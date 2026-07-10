namespace Mux.Search.Providers.Tavily
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed Tavily API response body.
    /// </summary>
    public class TavilyApiResponse
    {
        /// <summary>
        /// Gets or sets the echoed query.
        /// </summary>
        [JsonPropertyName("query")]
        public string? Query { get; set; }

        /// <summary>
        /// Gets or sets the answer text.
        /// </summary>
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }

        /// <summary>
        /// Gets or sets the request identifier.
        /// </summary>
        [JsonPropertyName("request_id")]
        public string? RequestId { get; set; }

        /// <summary>
        /// Gets or sets the response time.
        /// </summary>
        [JsonPropertyName("response_time")]
        public double? ResponseTime { get; set; }

        /// <summary>
        /// Gets or sets images returned for the response.
        /// </summary>
        [JsonPropertyName("images")]
        [JsonConverter(typeof(TavilyApiImageListConverter))]
        public List<TavilyApiImage>? Images { get; set; }

        /// <summary>
        /// Gets or sets web results.
        /// </summary>
        [JsonPropertyName("results")]
        public List<TavilyApiResult>? Results { get; set; }

        /// <summary>
        /// Gets or sets auto-selected parameters.
        /// </summary>
        [JsonPropertyName("auto_parameters")]
        public TavilyApiAutoParameters? AutoParameters { get; set; }

        /// <summary>
        /// Gets or sets usage metadata.
        /// </summary>
        [JsonPropertyName("usage")]
        public TavilyApiUsage? Usage { get; set; }
    }
}
