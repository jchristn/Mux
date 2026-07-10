namespace Mux.Search.Providers.You
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Typed You.com result sections.
    /// </summary>
    public class YouApiResults
    {
        /// <summary>
        /// Gets or sets web results.
        /// </summary>
        [JsonPropertyName("web")]
        public List<YouApiResult>? Web { get; set; }

        /// <summary>
        /// Gets or sets news results.
        /// </summary>
        [JsonPropertyName("news")]
        public List<YouApiResult>? News { get; set; }
    }
}
