namespace Mux.Core.Cloud
{
    using System;
    using MuxCloud.Protocol;

    /// <summary>
    /// Where someone without a Mux Cloud account creates one. Every sign-in form and command shows the same hint, and
    /// a rejected email or password adds it to the error, so a new user is never left at a sign-in prompt with nowhere
    /// to go.
    /// <para>Thread safety: stateless; safe to call concurrently.</para>
    /// </summary>
    public static class CloudSignup
    {
        #region Public-Members

        /// <summary>The path of the signup page on a Mux Cloud server.</summary>
        public const string SignupPath = "/signup";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The signup page for a Mux Cloud server, for example <c>https://app.usemux.ai/signup</c>.
        /// </summary>
        /// <param name="serverUrl">Mux Cloud server URL; blank uses <see cref="TunnelProtocol.DefaultServerUrl"/>.</param>
        /// <returns>Absolute signup URL.</returns>
        public static string Url(string? serverUrl)
        {
            string server = string.IsNullOrWhiteSpace(serverUrl) ? TunnelProtocol.DefaultServerUrl : serverUrl.Trim();
            return server.TrimEnd('/') + SignupPath;
        }

        /// <summary>
        /// The signup page without its scheme, for link text, for example <c>app.usemux.ai/signup</c>.
        /// </summary>
        /// <param name="serverUrl">Mux Cloud server URL; blank uses <see cref="TunnelProtocol.DefaultServerUrl"/>.</param>
        /// <returns>Signup URL without <c>https://</c> or <c>http://</c>.</returns>
        public static string DisplayUrl(string? serverUrl)
        {
            string url = Url(serverUrl);
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            return scheme >= 0 ? url.Substring(scheme + 3) : url;
        }

        /// <summary>
        /// The one-line hint every surface shows next to its sign-in form or command.
        /// </summary>
        /// <param name="serverUrl">Mux Cloud server URL; blank uses <see cref="TunnelProtocol.DefaultServerUrl"/>.</param>
        /// <returns>For example <c>No account yet? Create one at https://app.usemux.ai/signup</c>.</returns>
        public static string Hint(string? serverUrl)
        {
            return "No account yet? Create one at " + Url(serverUrl);
        }

        /// <summary>
        /// A sign-in error message, followed by the signup hint when the cloud rejected the email or password (HTTP 401).
        /// </summary>
        /// <param name="exception">The failure.</param>
        /// <param name="serverUrl">Mux Cloud server URL the sign-in went to; blank uses the default.</param>
        /// <returns>Message for the user.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="exception"/> is null.</exception>
        public static string ErrorMessage(Exception exception, string? serverUrl)
        {
            ArgumentNullException.ThrowIfNull(exception);
            if (exception is CloudEnrollmentException enrollment && enrollment.StatusCode == 401)
            {
                return exception.Message + " " + Hint(serverUrl);
            }

            return exception.Message;
        }

        #endregion
    }
}
