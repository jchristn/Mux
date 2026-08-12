namespace Mux.Core.Llm
{
    /// <summary>
    /// The outcome of attempting to load (or validate) a model at an endpoint by issuing a minimal request.
    /// A success means the backend accepted the request for the configured model; a failure carries the
    /// backend or transport error so the shell can surface why the model could not be loaded.
    /// </summary>
    public sealed class ModelLoadResult
    {
        #region Public-Members

        /// <summary>
        /// Whether the model was loaded / validated successfully.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// The failure details when <see cref="Success"/> is false; null on success.
        /// </summary>
        public string? Error { get; }

        #endregion

        #region Constructors-and-Factories

        private ModelLoadResult(bool success, string? error)
        {
            Success = success;
            Error = error;
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <returns>A success result.</returns>
        public static ModelLoadResult Ok()
        {
            return new ModelLoadResult(true, null);
        }

        /// <summary>
        /// Creates a failure result.
        /// </summary>
        /// <param name="error">The failure details.</param>
        /// <returns>A failure result carrying <paramref name="error"/>.</returns>
        public static ModelLoadResult Fail(string? error)
        {
            return new ModelLoadResult(false, string.IsNullOrWhiteSpace(error) ? "unknown error" : error);
        }

        #endregion
    }
}
