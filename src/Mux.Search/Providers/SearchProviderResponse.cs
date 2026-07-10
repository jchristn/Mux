namespace Mux.Search.Providers
{
    /// <summary>
    /// Raw and typed HTTP response returned from a search provider.
    /// </summary>
    /// <typeparam name="TBody">The typed response body.</typeparam>
    public class SearchProviderResponse<TBody>
    {
        #region Private-Members

        private TBody _Body;
        private string _RawJson = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchProviderResponse{TBody}"/> class.
        /// </summary>
        /// <param name="body">The typed response body.</param>
        /// <param name="rawJson">The raw JSON response body.</param>
        public SearchProviderResponse(TBody body, string rawJson)
        {
            _Body = body;
            _RawJson = rawJson ?? string.Empty;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The typed response body.
        /// </summary>
        public TBody Body
        {
            get => _Body;
            set => _Body = value;
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

    }
}
