namespace Mux.Core.Cloud
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Net.WebSockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using MuxCloud.Protocol;

    /// <summary>
    /// Forwards relayed traffic from the tunnel to this machine's local hub. HTTP responses stream back as
    /// <c>http.response.start</c>, chunks (sent as soon as bytes arrive, so server-sent events flow live), and
    /// <c>http.response.end</c>. WebSocket channels are bridged frame by frame. Every forwarded call carries the local
    /// API key plus the <c>X-Mux-Relay</c> and <c>X-Mux-Base-Path</c> headers.
    /// <para>Thread safety: safe to call concurrently; each stream or channel runs independently.</para>
    /// </summary>
    public sealed class CloudRelayForwarder : IDisposable
    {
        #region Private-Members

        private static readonly HashSet<string> _DropRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization", "Cookie", "X-Api-Key", TunnelProtocol.RelayHeader, TunnelProtocol.BasePathHeader, "X-Forwarded-For"
        };

        private readonly Func<CloudLocalEndpoint> _Endpoint;
        private readonly HttpClient _Http;
        private int _ChunkBytes = TunnelProtocol.MaxChunkBytes;
        private TimeSpan _WebSocketConnectTimeout = TimeSpan.FromSeconds(10);
        private bool _Disposed;

        #endregion

        #region Public-Members

        /// <summary>
        /// Largest body chunk per frame, in raw bytes. Default and maximum <see cref="TunnelProtocol.MaxChunkBytes"/>,
        /// minimum 1024.
        /// </summary>
        public int ChunkBytes
        {
            get => _ChunkBytes;
            set => _ChunkBytes = Math.Clamp(value, 1024, TunnelProtocol.MaxChunkBytes);
        }

        /// <summary>Timeout for opening a local WebSocket. Default 10 seconds.</summary>
        public TimeSpan WebSocketConnectTimeout
        {
            get => _WebSocketConnectTimeout;
            set => _WebSocketConnectTimeout = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : value;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="endpoint">Returns the current local endpoint (re-read per request so key or port changes apply).</param>
        /// <exception cref="ArgumentNullException">endpoint is null.</exception>
        public CloudRelayForwarder(Func<CloudLocalEndpoint> endpoint)
        {
            _Endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
            HttpClientHandler handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = System.Net.DecompressionMethods.None,
                // Loopback only: the local hub may use a self-signed certificate when rest.ssl is on.
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            _Http = new HttpClient(handler, true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Forward one relayed HTTP request and stream its response back.
        /// </summary>
        /// <param name="streamId">Stream identifier.</param>
        /// <param name="request">Relayed request.</param>
        /// <param name="sender">Tunnel sender.</param>
        /// <param name="token">Cancelled by <c>http.cancel</c> or tunnel loss; the local request is aborted.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">request or sender is null.</exception>
        public async Task HandleHttpAsync(string streamId, HttpRequestPayload request, ICloudTunnelSender sender, CancellationToken token)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            CloudLocalEndpoint endpoint = _Endpoint();
            bool started = false;
            try
            {
                using HttpRequestMessage message = BuildRequest(endpoint, request);
                using HttpResponseMessage response = await _Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);

                HttpResponseStartPayload start = new HttpResponseStartPayload
                {
                    StatusCode = (int)response.StatusCode,
                    ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                    Headers = TunnelHeaders.Filter(Flatten(response.Headers, response.Content.Headers), null)
                };
                start.Headers.Remove("Content-Type");
                if (!await sender.SendAsync(TunnelProtocol.CreateHttpResponseStart(streamId, start), token).ConfigureAwait(false)) return;
                started = true;

                using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                byte[] buffer = new byte[_ChunkBytes];
                while (true)
                {
                    int read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (read <= 0) break;
                    if (!await sender.SendAsync(TunnelProtocol.CreateHttpChunk(streamId, buffer, 0, read), token).ConfigureAwait(false)) return;
                }

                await sender.SendAsync(TunnelProtocol.CreateHttpResponseEnd(streamId), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Canceled by the cloud or the tunnel closed: send nothing further for this stream.
            }
            catch (FormatException e)
            {
                await TrySendErrorAsync(sender, streamId, TunnelErrorCodes.BadRequest, "The relayed request body is not valid base64: " + e.Message, 400).ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || e is IOException || e is InvalidOperationException || e is OperationCanceledException || e is UriFormatException)
            {
                string message = started
                    ? "The local mux response ended early: " + e.Message
                    : "Could not reach the local mux API at " + endpoint.BaseUrl + ": " + e.Message;
                await TrySendErrorAsync(sender, streamId, TunnelErrorCodes.LoopbackFailed, message, 502).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Bridge one relayed WebSocket channel to a local WebSocket until either side closes.
        /// </summary>
        /// <param name="channel">The cloud side of the channel.</param>
        /// <param name="open">Open request.</param>
        /// <param name="sender">Tunnel sender.</param>
        /// <param name="token">Cancelled when the tunnel closes.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public async Task HandleWebSocketAsync(CloudWsChannel channel, WsOpenPayload open, ICloudTunnelSender sender, CancellationToken token)
        {
            if (channel == null) throw new ArgumentNullException(nameof(channel));
            if (open == null) throw new ArgumentNullException(nameof(open));
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            CloudLocalEndpoint endpoint = _Endpoint();
            using ClientWebSocket local = new ClientWebSocket();
            if (!string.IsNullOrEmpty(endpoint.ApiKey)) local.Options.SetRequestHeader("Authorization", "Bearer " + endpoint.ApiKey);
            local.Options.SetRequestHeader(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
            if (!string.IsNullOrEmpty(endpoint.BasePath)) local.Options.SetRequestHeader(TunnelProtocol.BasePathHeader, endpoint.BasePath);
            if (!string.IsNullOrWhiteSpace(open.RequesterIp)) local.Options.SetRequestHeader("X-Forwarded-For", open.RequesterIp);
            local.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;

            try
            {
                using CancellationTokenSource connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                connectTimeout.CancelAfter(_WebSocketConnectTimeout);
                await local.ConnectAsync(endpoint.WebSocketUri(open.Path, open.Query), connectTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is UriFormatException || e is ArgumentException)
            {
                if (!token.IsCancellationRequested)
                {
                    await TrySendErrorAsync(sender, channel.Id, TunnelErrorCodes.LoopbackFailed, "Could not open the local mux WebSocket at " + endpoint.BaseUrl + open.Path + ": " + e.Message, 502).ConfigureAwait(false);
                }

                return;
            }

            if (!await sender.SendAsync(TunnelProtocol.CreateWsOpened(channel.Id), token).ConfigureAwait(false)) return;

            using CancellationTokenSource both = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task localToCloud = PumpLocalToCloudAsync(local, channel.Id, sender, both.Token);
            Task<bool> cloudToLocal = PumpCloudToLocalAsync(local, channel, both.Token);
            Task first = await Task.WhenAny(localToCloud, cloudToLocal).ConfigureAwait(false);
            bool cloudClosed = first == cloudToLocal && cloudToLocal.Result;
            both.Cancel();
            try { await Task.WhenAll(localToCloud, cloudToLocal).ConfigureAwait(false); } catch (Exception) { }

            if (cloudClosed)
            {
                // The cloud closed the channel: close the local socket with the client's code.
                try { await local.CloseOutputAsync(ToStatus(channel.CloseCode), channel.CloseReason, CancellationToken.None).ConfigureAwait(false); } catch (Exception) { }
            }
            else if (!token.IsCancellationRequested)
            {
                int code = local.CloseStatus.HasValue ? (int)local.CloseStatus.Value : 1000;
                await sender.SendAsync(TunnelProtocol.CreateWsClose(channel.Id, code, local.CloseStatusDescription ?? string.Empty), CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>Release the HTTP client.</summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            _Http.Dispose();
        }

        #endregion

        #region Private-Methods

        private static HttpRequestMessage BuildRequest(CloudLocalEndpoint endpoint, HttpRequestPayload request)
        {
            HttpRequestMessage message = new HttpRequestMessage(new HttpMethod(request.Method), endpoint.HttpUri(request.Path, request.Query));
            byte[] body = string.IsNullOrEmpty(request.BodyBase64) ? Array.Empty<byte>() : Convert.FromBase64String(request.BodyBase64);
            if (body.Length > 0 || !string.IsNullOrEmpty(request.ContentType))
            {
                message.Content = new ByteArrayContent(body);
                if (!string.IsNullOrEmpty(request.ContentType) && MediaTypeHeaderValue.TryParse(request.ContentType, out MediaTypeHeaderValue? type))
                {
                    message.Content.Headers.ContentType = type;
                }
            }

            foreach (KeyValuePair<string, string> header in TunnelHeaders.Filter(request.Headers, _DropRequestHeaders))
            {
                if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) continue;
                if (!message.Headers.TryAddWithoutValidation(header.Key, header.Value) && message.Content != null)
                {
                    message.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            if (!string.IsNullOrEmpty(endpoint.ApiKey)) message.Headers.TryAddWithoutValidation("Authorization", "Bearer " + endpoint.ApiKey);
            message.Headers.TryAddWithoutValidation(TunnelProtocol.RelayHeader, TunnelProtocol.RelayHeaderValue);
            if (!string.IsNullOrEmpty(endpoint.BasePath)) message.Headers.TryAddWithoutValidation(TunnelProtocol.BasePathHeader, endpoint.BasePath);
            if (!string.IsNullOrWhiteSpace(request.RequesterIp)) message.Headers.TryAddWithoutValidation("X-Forwarded-For", request.RequesterIp);
            return message;
        }

        private static IEnumerable<KeyValuePair<string, string>> Flatten(HttpResponseHeaders headers, HttpContentHeaders content)
        {
            foreach (KeyValuePair<string, IEnumerable<string>> header in headers.Concat(content))
            {
                yield return new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value));
            }
        }

        private static async Task PumpLocalToCloudAsync(ClientWebSocket local, string channelId, ICloudTunnelSender sender, CancellationToken token)
        {
            byte[] buffer = new byte[16 * 1024];
            StringBuilder text = new StringBuilder();
            while (!token.IsCancellationRequested && local.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await local.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is WebSocketException || e is OperationCanceledException)
                {
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close) return;
                if (result.MessageType != WebSocketMessageType.Text) continue;
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage) continue;
                string frame = text.ToString();
                text.Clear();
                if (!await sender.SendAsync(TunnelProtocol.CreateWsFrame(channelId, frame), token).ConfigureAwait(false)) return;
            }
        }

        // Returns true when the cloud closed the channel, false when the local socket failed or the tunnel closed.
        private static async Task<bool> PumpCloudToLocalAsync(ClientWebSocket local, CloudWsChannel channel, CancellationToken token)
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out string? frame))
                    {
                        byte[] bytes = Encoding.UTF8.GetBytes(frame ?? string.Empty);
                        await local.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
                    }
                }

                return true;
            }
            catch (Exception e) when (e is WebSocketException || e is OperationCanceledException || e is ObjectDisposedException)
            {
                return false;
            }
        }

        private static WebSocketCloseStatus ToStatus(int code)
        {
            // Codes the RFC reserves for the transport (1005, 1006, 1015) cannot be sent; use a normal close.
            if (code == 1005 || code == 1006 || code == 1015 || code < 1000 || code > 4999) return WebSocketCloseStatus.NormalClosure;
            return (WebSocketCloseStatus)code;
        }

        private static async Task TrySendErrorAsync(ICloudTunnelSender sender, string id, string code, string message, int status)
        {
            try
            {
                await sender.SendAsync(TunnelProtocol.CreateStreamError(id, code, message, status), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The tunnel is gone; nothing to report to.
            }
        }

        #endregion
    }
}
