namespace Mux.Core.Cloud
{
    using System;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Reads the Mux Cloud status from the local hub (<c>GET /v1.0/api/cloud/status</c>), for surfaces that do not run
    /// the tunnel themselves (the CLI, the terminal UI, the desktop app when the tray agent owns the hub).
    /// <para>Thread safety: stateless; safe to call concurrently.</para>
    /// </summary>
    public static class CloudHubStatusClient
    {
        #region Private-Members

        // The hub writes enums as names ("Connected"); the converter also accepts numbers.
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Ask the local hub for its Mux Cloud status.
        /// </summary>
        /// <param name="settings">Settings (the REST block gives the port and key); null loads them.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The hub's status, or null when no hub answers.</returns>
        public static async Task<CloudStatus?> GetAsync(MuxSettings? settings, CancellationToken token)
        {
            MuxSettings s = settings ?? SettingsLoader.LoadSettings();
            string url = (s.Rest.Ssl ? "https" : "http") + "://127.0.0.1:" + s.Rest.Port + "/v1.0/api/cloud/status";
            using HttpClientHandler handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
            using HttpClient http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            string? key = string.IsNullOrWhiteSpace(s.Rest.ApiKey) ? null : SettingsLoader.ExpandEnvironmentVariables(s.Rest.ApiKey);
            if (!string.IsNullOrEmpty(key)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + key);
            try
            {
                using HttpResponseMessage response = await http.SendAsync(request, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                return JsonSerializer.Deserialize<CloudStatus>(text, _JsonOptions);
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// The status to show: the hub's when it answers, otherwise one derived from settings.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Status.</returns>
        public static async Task<CloudStatus> GetOrOfflineAsync(CancellationToken token)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            CloudStatus? status = await GetAsync(settings, token).ConfigureAwait(false);
            return status ?? CloudAccountService.OfflineStatus(settings.Cloud, null);
        }

        #endregion
    }
}
