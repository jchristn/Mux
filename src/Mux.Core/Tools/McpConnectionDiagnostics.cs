namespace Mux.Core.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Security.Authentication;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;

    /// <summary>
    /// Explains why an MCP server connection failed. The MCP client library reports only success or failure, so for
    /// an HTTP server mux sends its own <c>initialize</c> request to the same URL with the same auth headers and
    /// reports what happened: the connection-level cause (connection refused, host not found, TLS failure, timeout,
    /// reset), or the HTTP status, notable headers, and the start of the response body. For a stdio server it reports
    /// whether the command resolves on PATH, the client's log, and the server's stderr. Header values are never
    /// printed, and they are masked wherever they appear in a body or log line.
    /// </summary>
    public static class McpConnectionDiagnostics
    {
        #region Private-Members

        private const int MaxBodyBytes = 8192;
        private const int MaxBodyChars = 2000;
        private const int MaxLogLines = 20;
        private const string Redacted = "<redacted>";
        private const string StderrPrefix = "[SERVER STDERR] ";

        private static readonly string[] _SensitiveHeaders = { "Set-Cookie", "Set-Cookie2", "Cookie", "Authorization", "Proxy-Authorization", "Proxy-Authenticate-Info", "X-Api-Key", "X-Auth-Token" };

        private static readonly string[] _NotableHeaders = { "WWW-Authenticate", "Retry-After", "Server", "Location" };

        #endregion

        #region Public-Members

        /// <summary>The default time the HTTP probe waits for a response.</summary>
        public static readonly TimeSpan DefaultProbeTimeout = TimeSpan.FromSeconds(10);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the URL the MCP client posts JSON-RPC requests to: the base URL without a trailing slash, then the path.
        /// </summary>
        /// <param name="baseUrl">The configured server URL.</param>
        /// <param name="mcpPath">The normalized MCP path (for example <c>/mcp</c>).</param>
        /// <returns>The RPC URL.</returns>
        public static string BuildRpcUrl(string baseUrl, string mcpPath)
        {
            return (baseUrl ?? string.Empty).TrimEnd('/') + (mcpPath ?? string.Empty);
        }

        /// <summary>
        /// Probes an HTTP MCP server that failed to connect and returns an exception describing the failure.
        /// </summary>
        /// <param name="config">The server configuration. Must not be null.</param>
        /// <param name="rpcUrl">The exact URL the client used.</param>
        /// <param name="headers">The auth headers the client sent (names and values; values are never printed).</param>
        /// <param name="clientLog">Messages the MCP client logged during the attempt, or null.</param>
        /// <param name="timeout">How long to wait for the probe's response.</param>
        /// <param name="cancellationToken">Cancels the probe.</param>
        /// <returns>The exception to throw.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        public static async Task<McpConnectionException> DiagnoseHttpAsync(
            McpServerConfig config,
            string rpcUrl,
            IReadOnlyDictionary<string, string>? headers,
            IReadOnlyList<string>? clientLog,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            IReadOnlyDictionary<string, string> sent = headers ?? new Dictionary<string, string>();
            List<string> secrets = sent.Values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            foreach (string value in sent.Values)
            {
                int space = value.IndexOf(' ');
                if (space > 0 && space < value.Length - 1) secrets.Add(value.Substring(space + 1));
            }

            string clientReason = FindClientReason(clientLog, "connection failed:", secrets);
            List<string> lines = new List<string>();
            string cause;

            if (!Uri.TryCreate(rpcUrl, UriKind.Absolute, out Uri? uri))
            {
                cause = "the URL is not valid";
                lines.Add("URL: " + rpcUrl);
                return Build(config, cause, lines, clientReason, null);
            }

            lines.Add("Request: POST " + rpcUrl + " (JSON-RPC initialize)");
            if (sent.Count > 0)
            {
                lines.Add("Auth headers sent: " + string.Join(", ", sent.Keys) + " (values hidden)");
            }

            Stopwatch watch = Stopwatch.StartNew();
            HttpResponseMessage? response = null;
            try
            {
                using (SocketsHttpHandler handler = new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = timeout })
                using (HttpClient client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan })
                using (CancellationTokenSource timeoutCts = new CancellationTokenSource(timeout))
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                using (HttpRequestMessage request = BuildInitializeRequest(uri, sent))
                {
                    try
                    {
                        response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        cause = Classify(ex, uri, timeout);
                        lines.Add("Result: " + cause + " after " + watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms");
                        string detail = Flatten(ex);
                        if (!string.IsNullOrEmpty(detail) && !cause.Contains(detail, StringComparison.Ordinal)) lines.Add("Error: " + detail);
                        lines.Add("Hint: " + HintForConnection(cause, uri));
                        return Build(config, cause, lines, clientReason, ex);
                    }

                    using (response)
                    {
                        McpProbeReply reply = await BuildReplyAsync(response, rpcUrl, watch.ElapsedMilliseconds, secrets, sent.Keys, linked.Token).ConfigureAwait(false);
                        AppendReplyLines(lines, reply);
                        if (!reply.IsSuccess)
                        {
                            cause = "HTTP " + reply.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + reply.ReasonPhrase;
                            lines.Add("Hint: " + HintForStatus(reply.StatusCode, config));
                        }
                        else
                        {
                            cause = DescribeSuccessBody(reply.Body, reply.ContentType.Length == 0 ? "(none)" : reply.ContentType, clientReason);
                            string? sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? ids) ? ids.FirstOrDefault() : null;
                            if (!string.IsNullOrEmpty(sessionId))
                            {
                                await EndSessionAsync(client, uri, sent, sessionId!, cancellationToken).ConfigureAwait(false);
                            }
                        }

                        return Build(config, cause, lines, clientReason, null, reply);
                    }
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                cause = Classify(ex, uri, timeout);
                lines.Add("Probe failed: " + Flatten(ex));
                return Build(config, cause, lines, clientReason, ex);
            }
        }

        /// <summary>
        /// Describes a stdio MCP server that failed to launch or complete its handshake.
        /// </summary>
        /// <param name="config">The server configuration. Must not be null.</param>
        /// <param name="clientLog">Messages the MCP client logged during the attempt (including server stderr), or null.</param>
        /// <param name="error">An exception raised by the attempt, or null.</param>
        /// <returns>The exception to throw.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="config"/> is null.</exception>
        public static McpConnectionException DiagnoseStdio(McpServerConfig config, IReadOnlyList<string>? clientLog, Exception? error)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            List<string> log = clientLog == null ? new List<string>() : new List<string>(clientLog);
            List<string> stderr = log.Where(l => l.StartsWith(StderrPrefix, StringComparison.Ordinal)).Select(l => l.Substring(StderrPrefix.Length)).ToList();
            List<string> other = log.Where(l => !l.StartsWith(StderrPrefix, StringComparison.Ordinal)).ToList();
            string command = config.Command ?? string.Empty;
            string? resolved = ResolveCommand(command);

            string launchFailure = FindClientReason(other, "Failed to launch MCP server:", null);
            string handshakeFailure = FindClientReason(other, "MCP initialize failed:", null);
            string cause;
            if (string.IsNullOrWhiteSpace(command))
            {
                cause = "no command is configured";
            }
            else if (resolved == null)
            {
                cause = "command '" + command + "' was not found" + (Path.IsPathRooted(command) ? string.Empty : " on PATH");
            }
            else if (!string.IsNullOrEmpty(handshakeFailure))
            {
                cause = "the process started but the MCP handshake failed: " + handshakeFailure;
            }
            else if (!string.IsNullOrEmpty(launchFailure))
            {
                cause = "the process could not start: " + launchFailure;
            }
            else if (error != null)
            {
                cause = Flatten(error);
            }
            else
            {
                cause = "the process exited or stopped responding before the MCP handshake finished";
            }

            List<string> lines = new List<string>
            {
                "Command: " + command + (config.Args.Count > 0 ? " " + string.Join(" ", config.Args.Select(QuoteArgument)) : string.Empty),
                "Resolved: " + (resolved ?? "not found (searched " + PathDirectories().Count.ToString(CultureInfo.InvariantCulture) + " PATH directories)")
            };

            List<string> logTail = other.Count > MaxLogLines ? other.GetRange(other.Count - MaxLogLines, MaxLogLines) : other;
            foreach (string line in logTail)
            {
                lines.Add("Client log: " + line);
            }

            if (stderr.Count > 0)
            {
                List<string> tail = stderr.Count > MaxLogLines ? stderr.GetRange(stderr.Count - MaxLogLines, MaxLogLines) : stderr;
                lines.Add("Server stderr (last " + tail.Count.ToString(CultureInfo.InvariantCulture) + " of " + stderr.Count.ToString(CultureInfo.InvariantCulture) + " lines):");
                foreach (string line in tail)
                {
                    lines.Add("  " + line);
                }
            }
            else
            {
                lines.Add("Server stderr: (nothing)");
            }

            if (resolved == null && !string.IsNullOrWhiteSpace(command))
            {
                lines.Add("Hint: install the command, use its full path, or start mux from a shell whose PATH includes it.");
            }

            string summary = "Failed to start MCP server '" + config.Name + "' (" + command + "): " + cause;
            return new McpConnectionException(summary, string.Join(Environment.NewLine, lines), error);
        }

        /// <summary>
        /// Classifies a connection-level exception into a short, specific cause.
        /// </summary>
        /// <param name="exception">The exception. Must not be null.</param>
        /// <param name="uri">The URL that was requested.</param>
        /// <param name="timeout">The timeout that applied, used in the timeout message.</param>
        /// <returns>The cause, for example "connection refused by localhost:8080 (nothing is listening on that port)".</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static string Classify(Exception exception, Uri uri, TimeSpan timeout)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (uri == null) throw new ArgumentNullException(nameof(uri));

            string endpoint = uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is SocketException socket)
                {
                    return ClassifySocket(socket.SocketErrorCode, uri, endpoint);
                }

                if (current is AuthenticationException authentication)
                {
                    return "TLS handshake with " + endpoint + " failed: " + authentication.Message;
                }
            }

            if (exception is HttpRequestException http)
            {
                switch (http.HttpRequestError)
                {
                    case HttpRequestError.NameResolutionError:
                        return "host not found: DNS lookup for '" + uri.Host + "' failed";
                    case HttpRequestError.SecureConnectionError:
                        return "TLS handshake with " + endpoint + " failed";
                    case HttpRequestError.ResponseEnded:
                        return "the server at " + endpoint + " closed the connection before sending a response";
                    case HttpRequestError.InvalidResponse:
                        return "the server at " + endpoint + " sent a response that is not valid HTTP";
                    case HttpRequestError.ConnectionError:
                        return "could not connect to " + endpoint;
                }
            }

            if (exception is OperationCanceledException || exception is TimeoutException)
            {
                return "no response from " + endpoint + " within " + Math.Round(timeout.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " s";
            }

            return exception.GetType().Name + ": " + exception.Message;
        }

        /// <summary>
        /// Finds an executable: a rooted or relative path that exists, or a name on PATH (with PATHEXT on Windows).
        /// </summary>
        /// <param name="command">The command name or path.</param>
        /// <returns>The full path, or null when it cannot be found.</returns>
        public static string? ResolveCommand(string? command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            string trimmed = command.Trim();
            List<string> extensions = new List<string> { string.Empty };
            if (OperatingSystem.IsWindows())
            {
                string pathExt = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                extensions.AddRange(pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries));
            }

            if (Path.IsPathRooted(trimmed) || trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
            {
                foreach (string extension in extensions)
                {
                    string candidate = Path.GetFullPath(trimmed + extension);
                    if (File.Exists(candidate)) return candidate;
                }

                return null;
            }

            foreach (string directory in PathDirectories())
            {
                foreach (string extension in extensions)
                {
                    try
                    {
                        string candidate = Path.Combine(directory, trimmed + extension);
                        if (File.Exists(candidate)) return candidate;
                    }
                    catch (ArgumentException)
                    {
                        // A malformed PATH entry; skip it.
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces every occurrence of the secrets in the text with a placeholder.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="secrets">The values to hide, or null.</param>
        /// <returns>The masked text.</returns>
        public static string Mask(string text, IEnumerable<string>? secrets)
        {
            string result = text ?? string.Empty;
            if (secrets == null)
            {
                return result;
            }

            foreach (string secret in secrets.Where(s => !string.IsNullOrEmpty(s) && s.Length >= 4).OrderByDescending(s => s.Length))
            {
                result = result.Replace(secret, Redacted, StringComparison.Ordinal);
            }

            return result;
        }

        #endregion

        #region Private-Methods

        private static McpConnectionException Build(McpServerConfig config, string cause, List<string> lines, string clientReason, Exception? inner, McpProbeReply? reply = null)
        {
            if (!string.IsNullOrEmpty(clientReason))
            {
                lines.Add("MCP client error: " + clientReason);
            }

            string summary = "Failed to connect to HTTP MCP server '" + config.Name + "' at " + config.Url + ": " + cause;
            return new McpConnectionException(summary, string.Join(Environment.NewLine, lines), inner, reply);
        }

        private static HttpRequestMessage BuildInitializeRequest(Uri uri, IReadOnlyDictionary<string, string> headers)
        {
            string payload = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = "mux-diagnostic",
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new { },
                    clientInfo = new { name = "mux-diagnostics", version = "1.0" }
                }
            });

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            foreach (KeyValuePair<string, string> header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return request;
        }

        // Captures the reply as data: status, content type and length, every header (sensitive values redacted and
        // secrets masked), the start of the body, and the timing.
        private static async Task<McpProbeReply> BuildReplyAsync(HttpResponseMessage response, string rpcUrl, long elapsedMs, IEnumerable<string> secrets, IEnumerable<string> sentHeaderNames, CancellationToken cancellationToken)
        {
            HashSet<string> sensitive = new HashSet<string>(_SensitiveHeaders, StringComparer.OrdinalIgnoreCase);
            foreach (string name in sentHeaderNames)
            {
                sensitive.Add(name);
            }

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> header in response.Headers.Concat(response.Content.Headers))
            {
                string value = string.Join(", ", header.Value);
                headers[header.Key] = sensitive.Contains(header.Key) ? Redacted : Mask(value, secrets);
            }

            McpProbeReply reply = new McpProbeReply
            {
                RequestMethod = "POST",
                RequestUrl = rpcUrl,
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? response.StatusCode.ToString() : response.ReasonPhrase!,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                ContentLength = response.Content.Headers.ContentLength,
                Headers = headers,
                ElapsedMs = elapsedMs
            };

            await ReadBodyAsync(response, reply, cancellationToken).ConfigureAwait(false);
            reply.Body = Mask(reply.Body, secrets);
            return reply;
        }

        // The human-readable lines for a reply: status, content type and length, the headers that explain MCP
        // failures (WWW-Authenticate, Mcp-Session-Id presence, Retry-After, Server, Location), and the body.
        private static void AppendReplyLines(List<string> lines, McpProbeReply reply)
        {
            lines.Add("Response: HTTP " + reply.StatusCode.ToString(CultureInfo.InvariantCulture) + " " + reply.ReasonPhrase + " in " + reply.ElapsedMs.ToString(CultureInfo.InvariantCulture) + " ms");
            lines.Add("Content-Type: " + (reply.ContentType.Length == 0 ? "(none)" : reply.ContentType));
            if (reply.ContentLength.HasValue)
            {
                lines.Add("Content-Length: " + reply.ContentLength.Value.ToString(CultureInfo.InvariantCulture));
            }

            foreach (string name in _NotableHeaders)
            {
                string? value = reply.GetHeader(name);
                if (!string.IsNullOrEmpty(value))
                {
                    lines.Add(name + ": " + value);
                }
            }

            lines.Add("Mcp-Session-Id: " + (string.IsNullOrEmpty(reply.GetHeader("Mcp-Session-Id")) ? "absent" : "present"));
            lines.Add(reply.Body.Length == 0 ? "Body: (empty)" : "Body" + (reply.BodyTruncated ? " (cut)" : string.Empty) + ": " + reply.Body);
        }

        private static async Task ReadBodyAsync(HttpResponseMessage response, McpProbeReply reply, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[MaxBodyBytes];
            int total = 0;
            bool truncated = false;
            bool stillStreaming = false;
            try
            {
                using (CancellationTokenSource readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    readCts.CancelAfter(TimeSpan.FromSeconds(3));
                    using (Stream stream = await response.Content.ReadAsStreamAsync(readCts.Token).ConfigureAwait(false))
                    {
                        while (total < buffer.Length)
                        {
                            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), readCts.Token).ConfigureAwait(false);
                            if (read == 0) break;
                            total += read;
                            if (IsEventStream(response) && Encoding.UTF8.GetString(buffer, 0, total).Contains("\n\n", StringComparison.Ordinal)) break;
                        }

                        truncated = total >= buffer.Length;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                stillStreaming = true;
            }
            catch (Exception ex)
            {
                reply.Body = "(could not read the body: " + ex.Message + ")";
                return;
            }

            string text = Encoding.UTF8.GetString(buffer, 0, total).Replace("\r", string.Empty).Trim();
            if (text.Length > MaxBodyChars)
            {
                text = text.Substring(0, MaxBodyChars);
                truncated = true;
            }

            if (stillStreaming) text += (text.Length == 0 ? string.Empty : " ") + "(the body was still streaming after 3 s)";
            reply.Body = text;
            reply.BodyTruncated = truncated || stillStreaming;
        }

        private static bool IsEventStream(HttpResponseMessage response)
        {
            string? mediaType = response.Content.Headers.ContentType?.MediaType;
            return string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeSuccessBody(string body, string contentType, string clientReason)
        {
            string json = body;
            if (contentType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                json = string.Join("\n", body.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Select(l => l.Substring(5).Trim()));
            }

            try
            {
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out JsonElement error))
                    {
                        string code = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out JsonElement c) ? c.ToString() : "?";
                        string message = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out JsonElement m) ? m.ToString() : error.ToString();
                        return "initialize returned JSON-RPC error " + code + ": " + message;
                    }

                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("result", out JsonElement result))
                    {
                        string version = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("protocolVersion", out JsonElement v) ? v.ToString() : "unknown";
                        return "the server answered initialize (protocol " + version + ") but the MCP client failed"
                            + (string.IsNullOrEmpty(clientReason) ? string.Empty : ": " + clientReason);
                    }

                    return "the response is JSON but not a JSON-RPC response";
                }
            }
            catch (JsonException)
            {
                return "HTTP success, but the response is not JSON-RPC (Content-Type " + contentType + ")";
            }
        }

        private static async Task EndSessionAsync(HttpClient client, Uri uri, IReadOnlyDictionary<string, string> headers, string sessionId, CancellationToken cancellationToken)
        {
            try
            {
                using (HttpRequestMessage delete = new HttpRequestMessage(HttpMethod.Delete, uri))
                using (CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(2));
                    delete.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
                    foreach (KeyValuePair<string, string> header in headers)
                    {
                        delete.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    using (HttpResponseMessage ignored = await client.SendAsync(delete, cts.Token).ConfigureAwait(false))
                    {
                    }
                }
            }
            catch (Exception)
            {
                // Ending the probe's session is a courtesy; failures do not matter.
            }
        }

        private static string ClassifySocket(SocketError error, Uri uri, string endpoint)
        {
            switch (error)
            {
                case SocketError.ConnectionRefused:
                    return "connection refused by " + endpoint + " (nothing is listening on that port)";
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                    return "host not found: DNS lookup for '" + uri.Host + "' failed";
                case SocketError.TimedOut:
                    return "connection to " + endpoint + " timed out";
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                case SocketError.NetworkDown:
                    return endpoint + " is unreachable (" + error + ")";
                case SocketError.ConnectionReset:
                case SocketError.ConnectionAborted:
                    return "the connection to " + endpoint + " was reset";
                case SocketError.AccessDenied:
                    return "access denied connecting to " + endpoint;
                default:
                    return "socket error " + error + " connecting to " + endpoint;
            }
        }

        private static string HintForConnection(string cause, Uri uri)
        {
            if (cause.StartsWith("connection refused", StringComparison.Ordinal)) return "start the MCP server, or check the host and port in the URL.";
            if (cause.StartsWith("host not found", StringComparison.Ordinal)) return "check the host name '" + uri.Host + "' and your DNS or VPN.";
            if (cause.StartsWith("TLS", StringComparison.Ordinal)) return "check the server's certificate, or use http:// if the server does not serve TLS.";
            if (cause.StartsWith("no response", StringComparison.Ordinal) || cause.Contains("timed out", StringComparison.Ordinal)) return "the server or a firewall is not answering; check that the server is running and reachable.";
            return "check that the server is running and the URL is correct.";
        }

        private static string HintForStatus(int status, McpServerConfig config)
        {
            switch (status)
            {
                case 401:
                case 403:
                    return "the server rejected the credentials; check the auth settings (bearer token or API key) for '" + config.Name + "'.";
                case 404:
                case 405:
                    return "the MCP endpoint path may be wrong (configured path '" + config.McpPath + "'); many servers use /mcp.";
                case 406:
                case 415:
                    return "the server did not accept a JSON-RPC POST; it may not be a streamable HTTP MCP server.";
                default:
                    if (status >= 300 && status < 400) return "the server redirects; use the final URL instead.";
                    if (status >= 500) return "the server failed while handling initialize; check its logs.";
                    return "check the server's documentation for its MCP URL and auth requirements.";
            }
        }

        private static string FindClientReason(IReadOnlyList<string>? log, string marker, IEnumerable<string>? secrets)
        {
            if (log == null)
            {
                return string.Empty;
            }

            for (int i = log.Count - 1; i >= 0; i--)
            {
                string line = log[i] ?? string.Empty;
                int index = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    return Mask(line.Substring(index + marker.Length).Trim(), secrets);
                }
            }

            return string.Empty;
        }

        private static string Flatten(Exception exception)
        {
            List<string> messages = new List<string>();
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
                {
                    messages.Add(current.Message);
                }
            }

            return string.Join(" -> ", messages);
        }

        private static List<string> PathDirectories()
        {
            return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        private static string QuoteArgument(string argument)
        {
            return argument.IndexOfAny(new[] { ' ', '"' }) >= 0 ? "\"" + argument.Replace("\"", "\\\"") + "\"" : argument;
        }

        #endregion
    }
}
