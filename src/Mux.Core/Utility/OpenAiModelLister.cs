namespace Mux.Core.Utility
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Discovers the models served by an OpenAI-compatible backend (OpenAI, vLLM, or any
    /// <c>openai_compatible</c> endpoint) by querying its models endpoint
    /// (<c>GET {baseUrl}/models</c>). The response follows the OpenAI list shape
    /// (<c>{ "object": "list", "data": [ { "id": "..." } ] }</c>); every model id is returned.
    /// </summary>
    public static class OpenAiModelLister
    {
        /// <summary>
        /// Fetches the ids of every model advertised by the OpenAI-compatible server at
        /// <paramref name="baseUrl"/>.
        /// </summary>
        /// <param name="baseUrl">The endpoint base URL. Typically ends with <c>/v1</c>; trailing slashes are tolerated.</param>
        /// <param name="headers">Optional request headers (for example an <c>Authorization</c> bearer token). May be null.</param>
        /// <param name="ignoreCertErrors">True to bypass TLS certificate validation.</param>
        /// <param name="cancellationToken">A token to cancel the request.</param>
        /// <returns>The distinct model ids, sorted case-insensitively. Never null.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="baseUrl"/> is null or blank.</exception>
        public static async Task<List<string>> ListModelsAsync(
            string baseUrl,
            IReadOnlyDictionary<string, string>? headers,
            bool ignoreCertErrors,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("Base URL is required.", nameof(baseUrl));
            }

            string url = NormalizeModelsUrl(baseUrl);

            using HttpClient http = MuxHttpClientFactory.Create(ignoreCertErrors);
            http.Timeout = TimeSpan.FromSeconds(30);

            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> header in headers)
                {
                    if (!string.IsNullOrWhiteSpace(header.Key))
                    {
                        // TryAddWithoutValidation accepts restricted headers such as Authorization that the
                        // typed HttpRequestHeaders API would otherwise reject.
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value ?? string.Empty);
                    }
                }
            }

            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return ParseModelIds(json);
        }

        /// <summary>
        /// Builds the models-discovery URL from an endpoint base URL: trims surrounding whitespace and
        /// trailing slashes, then appends <c>/models</c> when the base does not already end with it. OpenAI
        /// and vLLM base URLs end with <c>/v1</c>, yielding the conventional <c>/v1/models</c> path.
        /// </summary>
        /// <param name="baseUrl">The base URL to normalize.</param>
        /// <returns>The models-discovery URL, or an empty string when the input is null or blank.</returns>
        public static string NormalizeModelsUrl(string? baseUrl)
        {
            string trimmed = (baseUrl ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return string.Empty;
            }

            trimmed = trimmed.TrimEnd('/');
            if (trimmed.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }

            return trimmed + "/models";
        }

        private static List<string> ParseModelIds(string json)
        {
            List<string> ids = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("data", out JsonElement data)
                && data.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement model in data.EnumerateArray())
                {
                    if (model.ValueKind == JsonValueKind.Object
                        && model.TryGetProperty("id", out JsonElement id)
                        && id.ValueKind == JsonValueKind.String)
                    {
                        string? value = id.GetString();
                        if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
                        {
                            ids.Add(value);
                        }
                    }
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }
    }
}
