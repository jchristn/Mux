namespace Mux.Search.Models
{
    /// <summary>
    /// Represents an image associated with a search response or result.
    /// </summary>
    public class SearchImage
    {
        /// <summary>
        /// The image URL.
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Optional image description.
        /// </summary>
        public string? Description { get; set; }
    }
}
