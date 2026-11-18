namespace Mux.Core.Utility
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// Turns models discovered on an endpoint (see <see cref="EndpointModelLister"/>) into new endpoints: each
    /// is a copy of the source endpoint (adapter, URL, credentials, region, project, tuning) with its own model
    /// and a unique name. Shared by the TUI, the REST import route, and so the web dashboard and VS Code.
    /// </summary>
    public static class EndpointModelImport
    {
        #region Public-Methods

        /// <summary>
        /// True when <paramref name="model"/> is already configured on an endpoint with the same adapter and base
        /// URL as <paramref name="source"/>.
        /// </summary>
        /// <param name="source">The endpoint the model was discovered on.</param>
        /// <param name="model">The model id.</param>
        /// <param name="existing">The configured endpoints.</param>
        /// <returns>True when already configured.</returns>
        public static bool IsConfigured(EndpointConfig source, string model, IEnumerable<EndpointConfig> existing)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (existing == null) return false;
            string sourceUrl = NormalizeUrl(source.BaseUrl);
            foreach (EndpointConfig endpoint in existing)
            {
                if (endpoint != null
                    && endpoint.AdapterType == source.AdapterType
                    && string.Equals(NormalizeUrl(endpoint.BaseUrl), sourceUrl, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(endpoint.Model, model, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds one new endpoint per model not already configured. The copies are never the default endpoint.
        /// </summary>
        /// <param name="source">The endpoint the models were discovered on.</param>
        /// <param name="models">The chosen model ids.</param>
        /// <param name="existing">The configured endpoints (for duplicate models and names).</param>
        /// <returns>The new endpoints, in the order given.</returns>
        public static List<EndpointConfig> CreateEndpoints(EndpointConfig source, IEnumerable<string> models, IEnumerable<EndpointConfig> existing)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            List<EndpointConfig> current = existing == null ? new List<EndpointConfig>() : new List<EndpointConfig>(existing);
            HashSet<string> takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EndpointConfig endpoint in current)
            {
                if (endpoint != null) takenNames.Add(endpoint.Name);
            }

            List<EndpointConfig> created = new List<EndpointConfig>();
            if (models == null) return created;
            foreach (string raw in models)
            {
                string model = (raw ?? string.Empty).Trim();
                if (model.Length == 0 || IsConfigured(source, model, current))
                {
                    continue;
                }

                EndpointConfig copy = source.Clone();
                copy.Model = model;
                copy.IsDefault = false;
                copy.Name = UniqueName(model, takenNames);
                takenNames.Add(copy.Name);
                current.Add(copy);
                created.Add(copy);
            }

            return created;
        }

        /// <summary>
        /// Returns <paramref name="baseName"/>, or the first "<c>baseName (n)</c>" not in <paramref name="taken"/>.
        /// </summary>
        /// <param name="baseName">The preferred name.</param>
        /// <param name="taken">Names already in use (case-insensitive).</param>
        /// <returns>A unique name.</returns>
        public static string UniqueName(string baseName, ISet<string> taken)
        {
            string name = string.IsNullOrWhiteSpace(baseName) ? "model" : baseName.Trim();
            if (taken == null || !taken.Contains(name))
            {
                return name;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = name + " (" + suffix + ")";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string NormalizeUrl(string? url)
        {
            return (url ?? string.Empty).Trim().TrimEnd('/');
        }

        #endregion
    }
}
