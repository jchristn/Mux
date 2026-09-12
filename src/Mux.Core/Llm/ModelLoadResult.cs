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

        /// <summary>
        /// Whether the endpoint was actually reached. True on success and on any failure where the backend
        /// returned an HTTP status (the server, URL, and credentials are usable, but the probe request itself
        /// did not succeed — for example a reasoning model that cannot answer within the probe's tiny token
        /// budget). False only for transport-level failures (DNS, connection refused, TLS, timeout), which are
        /// the genuine "unreachable" case.
        /// </summary>
        public bool Reachable { get; }

        #endregion

        #region Constructors-and-Factories

        private ModelLoadResult(bool success, string? error, bool reachable)
        {
            Success = success;
            Error = error;
            Reachable = reachable;
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <returns>A success result.</returns>
        public static ModelLoadResult Ok()
        {
            return new ModelLoadResult(true, null, true);
        }

        /// <summary>
        /// Creates a failure result for a transport-level (unreachable) failure.
        /// </summary>
        /// <param name="error">The failure details.</param>
        /// <returns>A failure result carrying <paramref name="error"/>, marked unreachable.</returns>
        public static ModelLoadResult Fail(string? error)
        {
            return new ModelLoadResult(false, string.IsNullOrWhiteSpace(error) ? "unknown error" : error, false);
        }

        /// <summary>
        /// Creates a failure result, distinguishing a reachable endpoint (the backend answered with an error)
        /// from a transport-level failure.
        /// </summary>
        /// <param name="error">The failure details.</param>
        /// <param name="reachable">True when the backend returned an HTTP status.</param>
        /// <returns>A failure result carrying <paramref name="error"/> and reachability.</returns>
        public static ModelLoadResult Fail(string? error, bool reachable)
        {
            return new ModelLoadResult(false, string.IsNullOrWhiteSpace(error) ? "unknown error" : error, reachable);
        }

        #endregion
    }
}
