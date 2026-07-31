namespace Mux.Core.Utility
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Discovers the models installed on an Ollama server by querying its native tags endpoint
    /// (<c>GET {baseUrl}/api/tags</c>). Ollama does not classify tags as completion or embedding models,
    /// so every installed model name is returned; the caller is responsible for letting the user pick
    /// the completion models.
    /// </summary>
    public static class OllamaModelLister
    {
        /// <summary>
        /// Fetches the names of every model installed on the Ollama server at <paramref name="baseUrl"/>.
        /// </summary>
        /// <param name="baseUrl">The Ollama base URL. A trailing <c>/v1</c> and slashes are tolerated.</param>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation.</param>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <returns>The distinct model names, sorted case-insensitively. Never null.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is null or blank.</exception>
        public static async Task<List<string>> ListModelsAsync(
            string baseUrl,
            bool ignoreCertErrors,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            }

            string root = NormalizeBaseUrl(baseUrl);
            string url = root + "/api/tags";

            using HttpClient http = MuxHttpClientFactory.Create(ignoreCertErrors);
            http.Timeout = TimeSpan.FromSeconds(30);

            using HttpResponseMessage response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseModelNames(json);
        }

        /// <summary>
        /// Normalizes an Ollama base URL for both requests and storage: trims surrounding whitespace and
        /// trailing slashes, drops a trailing <c>/v1</c> (the native Ollama API lives at the server root,
        /// not under the OpenAI-compatible surface), and prepends <c>http://</c> when no scheme is present.
        /// </summary>
        /// <param name="baseUrl">The base URL to normalize.</param>
        /// <returns>The normalized base URL, or an empty string when the input is null or blank.</returns>
        public static string NormalizeBaseUrl(string? baseUrl)
        {
            string trimmed = (baseUrl ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = "http://" + trimmed;
            }

            trimmed = trimmed.TrimEnd('/');
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - "/v1".Length);
            }

            return trimmed;
        }

        private static List<string> ParseModelNames(string json)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("models", out JsonElement models)
                && models.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement model in models.EnumerateArray())
                {
                    if (model.ValueKind == JsonValueKind.Object
                        && model.TryGetProperty("name", out JsonElement name)
                        && name.ValueKind == JsonValueKind.String)
                    {
                        string? value = name.GetString();
                        if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                        {
                            names.Add(value);
                        }
                    }
                }
            }

            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }
    }
}
