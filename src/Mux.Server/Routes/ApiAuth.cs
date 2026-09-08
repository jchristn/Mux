namespace Mux.Server.Routes
{
    using System;
    using WatsonWebserver.Core;

    /// <summary>
    /// Single-local-key authentication. When no key is configured the server runs open (dev/no-auth mode);
    /// otherwise the request must present the key as <c>Authorization: Bearer &lt;key&gt;</c> or
    /// <c>X-Api-Key: &lt;key&gt;</c>.
    /// </summary>
    internal static class ApiAuth
    {
        /// <summary>
        /// Authorize a request. On failure, sets a 401 status on the response and returns false.
        /// </summary>
        /// <param name="ctx">Watson HTTP context.</param>
        /// <param name="apiKey">The configured key, or null/empty for no-auth mode.</param>
        /// <returns>True when authorized.</returns>
        public static bool Authorize(HttpContextBase ctx, string? apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) return true;

            string? provided = ctx.Request.Headers["X-Api-Key"];
            if (string.IsNullOrEmpty(provided))
            {
                string? auth = ctx.Request.Headers["Authorization"];
                if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    provided = auth.Substring("Bearer ".Length).Trim();
                }
            }

            if (!string.IsNullOrEmpty(provided) && string.Equals(provided, apiKey, StringComparison.Ordinal))
            {
                return true;
            }

            ctx.Response.StatusCode = 401;
            return false;
        }
    }
}
