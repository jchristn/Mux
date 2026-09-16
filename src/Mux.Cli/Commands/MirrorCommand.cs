namespace Mux.Cli.Commands
{
    using System;
    using System.Net.WebSockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// Implements <c>mux mirror</c>: connects to a running <c>mux serve</c> (or another surface's embedded
    /// server) over the WebSocket bridge and live-tails a run's canonical event stream to the terminal,
    /// read-only. Subscribe by session id (positional or <c>--session</c>) or by <c>--run</c> id. Point it at a
    /// server with <c>--url</c> (defaults to the configured <c>rest</c> host/port) and authenticate with
    /// <c>--api-key</c> (defaults to the configured key). Runs until the mirrored run finishes or Ctrl+C.
    /// </summary>
    public sealed class MirrorCommand
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Run the mirror until the run completes, the connection closes, or the user interrupts.
        /// </summary>
        /// <param name="args">Arguments after the <c>mirror</c> verb.</param>
        /// <param name="cancellationToken">Cancellation token from the host.</param>
        /// <returns>Process exit code (0 success, 2 usage error, 1 transport failure).</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            string? sessionId = null;
            string? runId = null;
            string? urlOverride = null;
            string? apiKeyOverride = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "-h":
                    case "--help":
                        PrintUsage();
                        return 0;
                    case "--session":
                        if (i + 1 < args.Length) sessionId = args[++i];
                        break;
                    case "--run":
                        if (i + 1 < args.Length) runId = args[++i];
                        break;
                    case "--url":
                        if (i + 1 < args.Length) urlOverride = args[++i];
                        break;
                    case "--api-key":
                        if (i + 1 < args.Length) apiKeyOverride = args[++i];
                        break;
                    default:
                        if (!a.StartsWith("-", StringComparison.Ordinal) && sessionId == null && runId == null)
                        {
                            sessionId = a;
                        }
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(sessionId) && string.IsNullOrWhiteSpace(runId))
            {
                Console.Error.WriteLine("mux mirror: a session id (positional or --session) or --run id is required.");
                PrintUsage();
                return 2;
            }

            MuxSettings settings = SettingsLoader.LoadSettings();
            RestServerSettings rest = settings.Rest;
            string baseUrl = !string.IsNullOrWhiteSpace(urlOverride)
                ? urlOverride!
                : (rest.Ssl ? "https" : "http") + "://" + rest.Hostname + ":" + rest.Port;
            string? apiKey = !string.IsNullOrWhiteSpace(apiKeyOverride) ? apiKeyOverride : rest.ApiKey;

            Uri wsUri = BuildWebSocketUri(baseUrl, apiKey);

            using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler onCancel = (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += onCancel;

            try
            {
                // Ensure a hub is running: if nothing answers at the target, start the tray agent (best-effort)
                // and retry the connect while it boots. A ClientWebSocket cannot be reconnected after a failed
                // attempt, so each retry uses a fresh socket.
                Mux.Cli.App.AgentLauncher.EnsureRunning();

                ClientWebSocket? socket = null;
                Exception? lastError = null;
                for (int attempt = 0; attempt < 20 && socket == null && !cts.Token.IsCancellationRequested; attempt++)
                {
                    ClientWebSocket candidate = new ClientWebSocket();
                    try
                    {
                        await candidate.ConnectAsync(wsUri, cts.Token).ConfigureAwait(false);
                        socket = candidate;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        candidate.Dispose();
                        try { await Task.Delay(300, cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                    }
                }

                if (socket == null)
                {
                    Console.Error.WriteLine("mux mirror: could not connect to " + baseUrl + " — " + (lastError?.Message ?? "no server is reachable."));
                    return 1;
                }

                using ClientWebSocket activeSocket = socket;

                string subscribe = runId != null
                    ? "{\"action\":\"subscribe\",\"runId\":" + JsonSerializer.Serialize(runId) + "}"
                    : "{\"action\":\"subscribe\",\"sessionId\":" + JsonSerializer.Serialize(sessionId) + "}";
                await activeSocket.SendAsync(Encoding.UTF8.GetBytes(subscribe), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);

                Console.WriteLine("Mirroring " + (runId != null ? "run " + runId : "session " + sessionId) + " on " + baseUrl + " — Ctrl+C to stop.");
                await ReceiveLoopAsync(activeSocket, cts.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine();
                Console.WriteLine("· mirror stopped.");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken token)
        {
            byte[] buffer = new byte[16384];
            StringBuilder message = new StringBuilder();

            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                string frame = message.ToString();
                message.Clear();
                if (RenderFrame(frame))
                {
                    break;
                }
            }
        }

        // Renders one canonical envelope frame to the terminal. Returns true when the run reached a terminal
        // state and the mirror should stop.
        private bool RenderFrame(string frame)
        {
            JsonElement root;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(frame);
                root = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return false;
            }

            string eventType = root.TryGetProperty("eventType", out JsonElement typeElement) ? (typeElement.GetString() ?? string.Empty) : string.Empty;
            switch (eventType)
            {
                case "server.connected":
                    Console.WriteLine("· connected (mux " + GetString(root, "version") + ")");
                    return false;
                case "error":
                    Console.WriteLine();
                    Console.WriteLine("! error: " + GetString(root, "message"));
                    return false;
                case "assistant_text":
                    Console.Write(GetString(root, "text"));
                    return false;
                case "assistant_thinking":
                    Console.Error.Write(GetString(root, "text"));
                    return false;
                case "tool_call_proposed":
                    Console.WriteLine();
                    Console.WriteLine("→ tool " + (root.TryGetProperty("toolCall", out JsonElement tc) ? GetString(tc, "name") : string.Empty));
                    return false;
                case "tool_call_completed":
                    bool ok = root.TryGetProperty("result", out JsonElement res) && res.TryGetProperty("success", out JsonElement s) && s.ValueKind == JsonValueKind.True;
                    Console.WriteLine("  " + (ok ? "✓" : "✗") + " " + GetString(root, "toolName") + " (" + GetLong(root, "elapsedMs") + "ms)");
                    return false;
                case "run_completed":
                    Console.WriteLine();
                    Console.WriteLine("· run " + GetString(root, "status"));
                    return true;
                default:
                    return false;
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static long GetLong(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Number
                ? value.GetInt64()
                : 0;
        }

        private static Uri BuildWebSocketUri(string baseUrl, string? apiKey)
        {
            string trimmed = baseUrl.TrimEnd('/');
            string wsBase = trimmed.StartsWith("https", StringComparison.OrdinalIgnoreCase)
                ? "wss" + trimmed.Substring("https".Length)
                : trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? "ws" + trimmed.Substring("http".Length)
                    : trimmed;
            string query = string.IsNullOrEmpty(apiKey) ? string.Empty : "?apiKey=" + Uri.EscapeDataString(apiKey);
            return new Uri(wsBase + "/v1.0/ws" + query);
        }

        private static void PrintUsage()
        {
            Console.WriteLine("Usage: mux mirror <sessionId> [--session <id>] [--run <id>] [--url <baseUrl>] [--api-key <key>]");
            Console.WriteLine("Live-tails a run happening on a running mux server (read-only). Defaults to the configured");
            Console.WriteLine("rest host/port and API key. Subscribe by session id (positional or --session) or by --run id.");
        }
    }
}
