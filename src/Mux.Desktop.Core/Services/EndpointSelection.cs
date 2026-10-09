namespace Mux.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Decides which endpoint the desktop endpoint picker shows after its list is reloaded (for example after an
    /// endpoint is added, edited, or deleted): the endpoint currently in use when it still exists, otherwise the
    /// default endpoint, otherwise the first one. Pure logic so it can be tested without a window.
    /// </summary>
    public static class EndpointSelection
    {
        #region Public-Methods

        /// <summary>
        /// Resolves the endpoint to select.
        /// </summary>
        /// <param name="endpoints">The freshly loaded endpoints. Null is treated as empty.</param>
        /// <param name="currentName">The name of the endpoint in use, or null when none is.</param>
        /// <returns>The endpoint to select, or null when the list is empty.</returns>
        public static EndpointConfig? Resolve(IReadOnlyList<EndpointConfig>? endpoints, string? currentName)
        {
            if (endpoints == null || endpoints.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(currentName))
            {
                foreach (EndpointConfig endpoint in endpoints)
                {
                    if (endpoint != null && string.Equals(endpoint.Name, currentName, StringComparison.Ordinal))
                    {
                        return endpoint;
                    }
                }

                foreach (EndpointConfig endpoint in endpoints)
                {
                    if (endpoint != null && string.Equals(endpoint.Name, currentName, StringComparison.OrdinalIgnoreCase))
                    {
                        return endpoint;
                    }
                }
            }

            foreach (EndpointConfig endpoint in endpoints)
            {
                if (endpoint != null && endpoint.IsDefault)
                {
                    return endpoint;
                }
            }

            foreach (EndpointConfig endpoint in endpoints)
            {
                if (endpoint != null)
                {
                    return endpoint;
                }
            }

            return null;
        }

        #endregion
    }
}
