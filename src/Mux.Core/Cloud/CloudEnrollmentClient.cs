namespace Mux.Core.Cloud
{
    using System;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using MuxCloud.Protocol;

    /// <summary>
    /// Exchanges an account email and password for a machine credential (<c>POST /v1.0/api/machines/enroll</c>),
    /// and signs the machine out again (<c>POST /v1.0/api/machines/sign-out</c>). The password is sent once and never
    /// stored.
    /// <para>Thread safety: safe to call concurrently.</para>
    /// </summary>
    public sealed class CloudEnrollmentClient
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        private readonly HttpMessageHandler? _Handler;
        private TimeSpan _Timeout = TimeSpan.FromSeconds(30);

        #endregion

        #region Public-Members

        /// <summary>Request timeout. Default 30 seconds, minimum 1 second, maximum 5 minutes.</summary>
        public TimeSpan Timeout
        {
            get => _Timeout;
            set => _Timeout = value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : value;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="handler">Optional HTTP handler (tests); null uses a default handler.</param>
        public CloudEnrollmentClient(HttpMessageHandler? handler = null)
        {
            _Handler = handler;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enroll this machine.
        /// </summary>
        /// <param name="serverUrl">Mux Cloud server URL; blank uses <see cref="TunnelProtocol.DefaultServerUrl"/>.</param>
        /// <param name="request">Enrollment request.</param>
        /// <param name="allowInvalidCertificates">Accept invalid TLS certificates (self-hosted development).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The machine credential.</returns>
        /// <exception cref="ArgumentNullException">request is null.</exception>
        /// <exception cref="ArgumentException">The email or password is blank, or the URL is invalid.</exception>
        /// <exception cref="CloudEnrollmentException">The cloud refused the request or could not be reached.</exception>
        public async Task<CloudEnrollmentResponse> EnrollAsync(string? serverUrl, CloudEnrollmentRequest request, bool allowInvalidCertificates, CancellationToken token)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.Email)) throw new ArgumentException("An email address is required to sign in to Mux Cloud.", nameof(request));
            if (string.IsNullOrEmpty(request.Password)) throw new ArgumentException("A password is required to sign in to Mux Cloud.", nameof(request));
            Uri uri = TunnelProtocol.ToHttpUri(serverUrl, TunnelProtocol.EnrollPath);

            HttpMessageHandler handler = _Handler ?? CreateHandler(allowInvalidCertificates);
            using HttpClient http = new HttpClient(handler, _Handler == null) { Timeout = _Timeout };
            string body = JsonSerializer.Serialize(request);
            using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(message, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException)
            {
                throw new CloudEnrollmentException("Could not reach Mux Cloud at " + uri.GetLeftPart(UriPartial.Authority) + ": " + e.Message, 0, "Unreachable", e);
            }

            using (response)
            {
                string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    string code = string.Empty;
                    string detail = string.Empty;
                    try
                    {
                        using JsonDocument doc = JsonDocument.Parse(text);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            if (doc.RootElement.TryGetProperty("error", out JsonElement e) && e.ValueKind == JsonValueKind.String) code = e.GetString() ?? string.Empty;
                            if (doc.RootElement.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String) detail = m.GetString() ?? string.Empty;
                        }
                    }
                    catch (JsonException)
                    {
                    }

                    throw new CloudEnrollmentException(Explain(status, detail), status, code);
                }

                CloudEnrollmentResponse? result;
                try
                {
                    result = JsonSerializer.Deserialize<CloudEnrollmentResponse>(text, _JsonOptions);
                }
                catch (JsonException e)
                {
                    throw new CloudEnrollmentException("Mux Cloud returned an unreadable enrollment response.", status, "BadResponse", e);
                }

                if (result == null || string.IsNullOrWhiteSpace(result.MachineId) || string.IsNullOrWhiteSpace(result.MachineSecret))
                {
                    throw new CloudEnrollmentException("Mux Cloud returned an enrollment response without a machine credential.", status, "BadResponse");
                }

                return result;
            }
        }

        /// <summary>
        /// Tell Mux Cloud this machine is signing out (<c>POST /v1.0/api/machines/sign-out</c>), so the cloud revokes it
        /// instead of listing it as offline. Authenticated by a sign-out proof over the machine secret; no password or
        /// token is sent. Best effort: never throws for network or server failures.
        /// </summary>
        /// <param name="serverUrl">Mux Cloud server URL; blank uses <see cref="TunnelProtocol.DefaultServerUrl"/>.</param>
        /// <param name="machineId">Machine identifier.</param>
        /// <param name="machineSecret">Machine secret.</param>
        /// <param name="allowInvalidCertificates">Accept invalid TLS certificates (self-hosted development).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the cloud confirmed (204), or no longer recognizes the machine (401, already revoked or removed).</returns>
        public async Task<bool> SignOutAsync(string? serverUrl, string machineId, string machineSecret, bool allowInvalidCertificates, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(machineId) || string.IsNullOrWhiteSpace(machineSecret)) return false;
            try
            {
                Uri uri = TunnelProtocol.ToHttpUri(serverUrl, TunnelProtocol.SignOutPath);
                HttpMessageHandler handler = _Handler ?? CreateHandler(allowInvalidCertificates);
                using HttpClient http = new HttpClient(handler, _Handler == null) { Timeout = _Timeout };
                string body = TunnelProtocol.SerializeSignOut(MachineSignOutRequest.Create(machineId, machineSecret, DateTime.UtcNow));
                using HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                using HttpResponseMessage response = await http.SendAsync(message, token).ConfigureAwait(false);
                int status = (int)response.StatusCode;
                return status == 204 || status == 200 || status == 401;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private static HttpMessageHandler CreateHandler(bool allowInvalidCertificates)
        {
            HttpClientHandler handler = new HttpClientHandler();
            if (allowInvalidCertificates)
            {
                handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            return handler;
        }

        private static string Explain(int status, string detail)
        {
            string prefix = status switch
            {
                400 => "Mux Cloud rejected the request",
                401 => "The email or password is not correct",
                403 => "Mux Cloud refused to add this machine",
                404 => "This server does not look like Mux Cloud (no enrollment route)",
                409 => "Mux Cloud could not add this machine",
                429 => "Too many sign-in attempts. Wait a few minutes and try again",
                _ => "Mux Cloud returned HTTP " + status
            };
            return string.IsNullOrWhiteSpace(detail) ? prefix + "." : prefix + ": " + detail;
        }

        #endregion
    }
}
