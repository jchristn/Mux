namespace Mux.Search.Models
{
    /// <summary>
    /// Describes one provider attempt during a normalized search request.
    /// </summary>
    public class WebSearchProviderAttempt
    {
        /// <summary>
        /// Configured provider name.
        /// </summary>
        public string ProviderName { get; set; } = string.Empty;

        /// <summary>
        /// Provider type.
        /// </summary>
        public string ProviderType { get; set; } = string.Empty;

        /// <summary>
        /// Whether the attempt succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Attempt detail or error message.
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}
