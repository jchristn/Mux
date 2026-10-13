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

        /// <summary>Finds only the named endpoint, without a default or first-item fallback.</summary>
        /// <param name="endpoints">Available endpoints.</param>
        /// <param name="name">The full configured endpoint name.</param>
        /// <returns>The matching endpoint, or null.</returns>
        public static EndpointConfig? FindByName(IReadOnlyList<EndpointConfig>? endpoints, string? name)
        {
            if (endpoints == null || string.IsNullOrWhiteSpace(name)) return null;
            string requested = name.Trim();
            foreach (EndpointConfig endpoint in endpoints)
                if (endpoint != null && string.Equals(endpoint.Name, requested, StringComparison.Ordinal)) return endpoint;
            foreach (EndpointConfig endpoint in endpoints)
                if (endpoint != null && string.Equals(endpoint.Name, requested, StringComparison.OrdinalIgnoreCase)) return endpoint;
            return null;
        }

        /// <summary>Completes configured endpoint names in the singular /endpoint command.</summary>
        /// <param name="text">Composer text.</param>
        /// <param name="caret">Caret offset.</param>
        /// <param name="endpoints">Available endpoints.</param>
        /// <param name="limit">Maximum suggestions.</param>
        /// <returns>Full slash command suggestions ready to complete without executing.</returns>
        public static List<string> CompleteCommand(string text, int caret, IReadOnlyList<EndpointConfig>? endpoints, int limit = 12)
        {
            List<string> names = new List<string>();
            if (endpoints != null)
                foreach (EndpointConfig endpoint in endpoints) if (endpoint != null) names.Add(endpoint.Name);
            return Mux.Core.Interaction.SlashCommandCompletion.MatchArgument(text, caret, "endpoint", names, limit);
        }

        #endregion
    }
}
