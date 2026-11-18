namespace Test.Shared.Support
{
    using System;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A scripted OpenAI-compatible chat server that asks for one <c>run_process</c> tool call and then reports whether
    /// the tool really ran. The first turn streams a tool call for <c>echo auto-tool-ran</c>; the turn after a tool
    /// result streams <c>TOOL_OUTPUT_SEEN</c> when the result contains the echoed text, otherwise
    /// <c>TOOL_OUTPUT_MISSING</c> (a denied tool). Mirrors the MuxCloud end-to-end fake model.
    /// </summary>
    internal sealed class ScriptedToolCallLlm : IDisposable
    {
        #region Public-Members

        /// <summary>The base URL to configure as an OpenAI-compatible endpoint, ending in <c>/v1</c>.</summary>
        public string BaseUrl { get; }

        /// <summary>The number of chat completion requests served.</summary>
        public int CompletionRequests
        {
            get
            {
                lock (_Lock)
                {
                    return _CompletionRequests;
                }
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cancellation = new CancellationTokenSource();
        private readonly Task _Loop;
        private readonly object _Lock = new object();
        private int _CompletionRequests = 0;

        #endregion

        #region Constructors-and-Factories

        private ScriptedToolCallLlm(HttpListener listener, string prefix)
        {
            _Listener = listener;
            BaseUrl = prefix.TrimEnd('/') + "/v1";
            _Loop = Task.Run(ListenLoopAsync);
        }

        /// <summary>
        /// Starts the server on a free loopback port.
        /// </summary>
        /// <returns>The started server.</returns>
        public static ScriptedToolCallLlm Start()
        {
            for (int attempt = 1; ; attempt++)
            {
                int port = FreePort();
                string prefix = "http://127.0.0.1:" + port + "/";
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                try
                {
                    listener.Start();
                    return new ScriptedToolCallLlm(listener, prefix);
                }
                catch (Exception ex) when ((ex is HttpListenerException || ex is SocketException) && attempt < 10)
                {
                    try { listener.Close(); } catch (Exception) { }
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>Stops the server.</summary>
        public void Dispose()
        {
            _Cancellation.Cancel();
            try { _Listener.Stop(); } catch (Exception) { }
            try { _Listener.Close(); } catch (Exception) { }
            try { _Loop.Wait(TimeSpan.FromSeconds(2)); } catch (Exception) { }
            _Cancellation.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task ListenLoopAsync()
        {
            while (!_Cancellation.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(context));
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? string.Empty;
                if (context.Request.HttpMethod == "GET" && path.EndsWith("/models", StringComparison.Ordinal))
                {
                    await WriteAsync(context, "application/json", "{\"object\":\"list\",\"data\":[{\"id\":\"scripted\",\"object\":\"model\"}]}").ConfigureAwait(false);
                    return;
                }

                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                lock (_Lock)
                {
                    _CompletionRequests++;
                }

                string lastRole = string.Empty;
                string lastContent = string.Empty;
                using (JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    if (doc.RootElement.TryGetProperty("messages", out JsonElement messages) && messages.ValueKind == JsonValueKind.Array && messages.GetArrayLength() > 0)
                    {
                        JsonElement last = messages[messages.GetArrayLength() - 1];
                        lastRole = last.TryGetProperty("role", out JsonElement role) ? role.GetString() ?? string.Empty : string.Empty;
                        lastContent = last.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.String ? content.GetString() ?? string.Empty : string.Empty;
                    }
                }

                StringBuilder sse = new StringBuilder();
                if (lastRole == "tool")
                {
                    string outcome = lastContent.Contains("auto-tool-ran", StringComparison.Ordinal) ? "TOOL_OUTPUT_SEEN" : "TOOL_OUTPUT_MISSING";
                    sse.Append(Chunk("{\"content\":\"Tool finished. " + outcome + "\"}", null));
                    sse.Append(Chunk("{}", "stop"));
                }
                else
                {
                    string arguments = JsonSerializer.Serialize("{\"command\":\"echo auto-tool-ran\"}");
                    sse.Append(Chunk("{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_auto_1\",\"type\":\"function\",\"function\":{\"name\":\"run_process\",\"arguments\":" + arguments + "}}]}", null));
                    sse.Append(Chunk("{}", "tool_calls"));
                }

                sse.Append("data: [DONE]\n\n");
                await WriteAsync(context, "text/event-stream", sse.ToString()).ConfigureAwait(false);
            }
            catch (Exception)
            {
                try { context.Response.Abort(); } catch (Exception) { }
            }
        }

        private static string Chunk(string delta, string? finish)
        {
            string finishJson = finish == null ? "null" : "\"" + finish + "\"";
            return "data: {\"id\":\"chunk\",\"object\":\"chat.completion.chunk\",\"model\":\"scripted\",\"choices\":[{\"index\":0,\"delta\":" + delta + ",\"finish_reason\":" + finishJson + "}]}\n\n";
        }

        private static async Task WriteAsync(HttpListenerContext context, string contentType, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            context.Response.StatusCode = 200;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static int FreePort()
        {
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        #endregion
    }
}
