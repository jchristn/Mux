namespace Mux.Search.Providers
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// Raw and parsed HTTP response returned from a search provider.
    /// </summary>
    public class SearchProviderResponse : IDisposable
    {
        #region Private-Members

        private JsonDocument _Document;
        private string _RawJson = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchProviderResponse"/> class.
        /// </summary>
        /// <param name="document">The parsed JSON response body.</param>
        /// <param name="rawJson">The raw JSON response body.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="document"/> is null.</exception>
        public SearchProviderResponse(JsonDocument document, string rawJson)
        {
            _Document = document ?? throw new ArgumentNullException(nameof(document));
            _RawJson = rawJson ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The parsed JSON response body.
        /// </summary>
        public JsonDocument Document
        {
            get => _Document;
            set => _Document = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// The raw JSON response body.
        /// </summary>
        public string RawJson
        {
            get => _RawJson;
            set => _RawJson = value ?? string.Empty;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Releases the parsed JSON document.
        /// </summary>
        public void Dispose()
        {
            _Document.Dispose();
        }

        #endregion
    }
}
