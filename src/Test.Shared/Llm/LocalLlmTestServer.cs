namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// An in-process loopback HTTP server that emulates the two backend wire protocols mux can reach
    /// through PolyPrompt: the OpenAI-compatible <c>/v1/chat/completions</c> endpoint (SSE streaming) and
    /// the Ollama-native <c>/api/chat</c> endpoint (newline-delimited JSON streaming). It drives responses
    /// from markers in the request (a <c>get_weather</c> tool triggers tool-call responses; message text
    /// containing <c>http500</c> or <c>unsupported</c> triggers the corresponding failures) so the mux
    /// <c>LlmClient</c> bridge can be exercised deterministically without a real model. Request bodies,
    /// paths, and headers are recorded for assertions.
    /// </summary>
    internal sealed class LocalLlmTestServer : IDisposable
    {
        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cancellation = new CancellationTokenSource();
        private readonly Task _Loop;
        private readonly object _Sync = new object();
        private readonly List<string> _RequestBodies = new List<string>();
        private readonly List<string> _RequestPaths = new List<string>();
        private readonly List<Dictionary<string, string>> _RequestHeaders = new List<Dictionary<string, string>>();
        private bool _Disposed = false;

        #endregion

        #region Public-Members

        /// <summary>
        /// Base endpoint URL for the local server (no trailing slash).
        /// </summary>
        public string Endpoint { get; }

        /// <summary>
        /// A detached snapshot of the request bodies received so far.
        /// </summary>
        public List<string> RequestBodies
        {
            get
            {
                lock (_Sync)
                {
                    return new List<string>(_RequestBodies);
                }
            }
        }

        /// <summary>
        /// A detached snapshot of the request paths received so far.
        /// </summary>
        public List<string> RequestPaths
        {
            get
            {
                lock (_Sync)
                {
                    return new List<string>(_RequestPaths);
                }
            }
        }

        /// <summary>
        /// The number of requests received so far.
        /// </summary>
        public int RequestCount
        {
            get
            {
                lock (_Sync)
                {
                    return _RequestBodies.Count;
                }
            }
        }

        #endregion

        #region Constructors-and-Factories

        private LocalLlmTestServer(HttpListener listener, string endpoint)
        {
            _Listener = listener;
            Endpoint = endpoint.TrimEnd('/');
            _Loop = Task.Run(ListenLoopAsync);
        }

        /// <summary>
        /// Starts a new local server on a free loopback port.
        /// </summary>
        /// <returns>The started server.</returns>
        public static LocalLlmTestServer Start()
        {
            // GetFreePort closes its probe socket before we bind, so another listener can grab the port in
            // between (a TOCTOU race that flakes on busy CI). Retry with a fresh port on a bind failure.
            const int attempts = 10;
            for (int attempt = 1; ; attempt++)
            {
                int port = GetFreePort();
                string prefix = "http://127.0.0.1:" + port + "/";
                HttpListener listener = new HttpListener();
                listener.Prefixes.Add(prefix);
                try
                {
                    listener.Start();
                }
                catch (Exception ex) when ((ex is HttpListenerException || ex is SocketException) && attempt < attempts)
                {
                    try { listener.Close(); } catch { }
                    continue;
                }

                return new LocalLlmTestServer(listener, prefix);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the first value of the named header seen on any recorded request, or null.
        /// </summary>
        /// <param name="name">The header name (case-insensitive).</param>
        /// <returns>The header value, or null when it was never seen.</returns>
        public string? HeaderValue(string name)
        {
            lock (_Sync)
            {
                foreach (Dictionary<string, string> headers in _RequestHeaders)
                {
                    if (headers.TryGetValue(name, out string? value))
                    {
                        return value;
                    }
                }
            }

            return null;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            _Cancellation.Cancel();
            try { _Listener.Stop(); } catch { }
            try { _Listener.Close(); } catch { }
            try { _Loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _Cancellation.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task ListenLoopAsync()
        {
            while (!_Cancellation.IsCancellationRequested)
            {
                try
                {
                    HttpListenerContext context = await _Listener.GetContextAsync().WaitAsync(_Cancellation.Token).ConfigureAwait(false);
                    _ = Task.Run(() => HandleContextAsync(context));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    if (_Cancellation.IsCancellationRequested) break;
                }
            }
        }

        private async Task HandleContextAsync(HttpListenerContext context)
        {
            try
            {
                string path = context.Request.Url?.AbsolutePath ?? string.Empty;
                string body;
                using (StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string? key in context.Request.Headers.AllKeys)
                {
                    if (key != null)
                    {
                        headers[key] = context.Request.Headers[key] ?? string.Empty;
                    }
                }

                lock (_Sync)
                {
                    _RequestBodies.Add(body);
                    _RequestPaths.Add(path);
                    _RequestHeaders.Add(headers);
                }

                bool stream = body.Contains("\"stream\":true", StringComparison.Ordinal);
                bool hasTools = body.Contains("get_weather", StringComparison.Ordinal);
                bool wantError = body.Contains("http500", StringComparison.OrdinalIgnoreCase);
                bool wantUnsupported = body.Contains("unsupported", StringComparison.OrdinalIgnoreCase);
                bool wantReasoning = body.Contains("reasoncapture", StringComparison.OrdinalIgnoreCase);

                if (wantError)
                {
                    await WriteJsonAsync(context, 500, "{\"error\":\"internal server error\"}").ConfigureAwait(false);
                    return;
                }

                if (wantUnsupported && hasTools)
                {
                    await WriteJsonAsync(context, 400, "{\"error\":\"this model does not support tools\"}").ConfigureAwait(false);
                    return;
                }

                if (path == "/v1/chat/completions")
                {
                    await HandleOpenAiAsync(context, stream, hasTools, wantReasoning).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/chat")
                {
                    await HandleOllamaAsync(context, stream, hasTools).ConfigureAwait(false);
                    return;
                }

                if (path == "/v1/models")
                {
                    // OpenAI-compatible model-discovery endpoint. Returns a fixed, intentionally unsorted set
                    // so the model lister's sorting and id extraction can be exercised deterministically.
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"object\":\"list\",\"data\":[{\"id\":\"gpt-4o-mini\",\"object\":\"model\"},{\"id\":\"gpt-4o\",\"object\":\"model\"},{\"id\":\"o1-preview\",\"object\":\"model\"}]}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/tags")
                {
                    // Ollama's model-discovery endpoint. Returns a fixed, intentionally unsorted set so the
                    // model importer's sorting and name extraction can be exercised deterministically.
                    await WriteJsonAsync(
                        context,
                        200,
                        "{\"models\":[{\"name\":\"qwen2.5-coder:7b\",\"model\":\"qwen2.5-coder:7b\"},{\"name\":\"llama3:latest\",\"model\":\"llama3:latest\"},{\"name\":\"nomic-embed-text:latest\",\"model\":\"nomic-embed-text:latest\"}]}").ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, 404, "{\"error\":\"not found\"}").ConfigureAwait(false);
            }
            catch
            {
                try { context.Response.Abort(); } catch { }
            }
        }

        private async Task HandleOpenAiAsync(HttpListenerContext context, bool stream, bool hasTools, bool wantReasoning = false)
        {
            if (stream && hasTools)
            {
                await BeginStream(context, "text/event-stream").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-1\",\"choices\":[{\"delta\":{\"role\":\"assistant\",\"content\":\"Checking \"},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-1\",\"choices\":[{\"delta\":{\"content\":\"weather. \"},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-1\",\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"call-weather-1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"\"}}]},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-1\",\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"{\\\"city\\\":\\\"Sea\"}}]},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-1\",\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"function\":{\"arguments\":\"ttle\\\"}\"}}]},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-1\",\"choices\":[{\"delta\":{},\"index\":0,\"finish_reason\":\"tool_calls\"}],\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":7,\"total_tokens\":18}}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: [DONE]\n\n").ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (stream)
            {
                await BeginStream(context, "text/event-stream").ConfigureAwait(false);
                if (wantReasoning)
                {
                    await WriteChunkAsync(context, "data: {\"id\":\"cc-2\",\"choices\":[{\"delta\":{\"reasoning_content\":\"Let me \"},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                    await WriteChunkAsync(context, "data: {\"id\":\"cc-2\",\"choices\":[{\"delta\":{\"reasoning_content\":\"think.\"},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                }
                await WriteChunkAsync(context, "data: {\"id\":\"cc-2\",\"choices\":[{\"delta\":{\"content\":\"hello \"},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-2\",\"choices\":[{\"delta\":{\"content\":\"world\"},\"index\":0,\"finish_reason\":null}]}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: {\"id\":\"cc-2\",\"choices\":[{\"delta\":{},\"index\":0,\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":3,\"completion_tokens\":2,\"total_tokens\":5}}\n\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "data: [DONE]\n\n").ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (hasTools)
            {
                await WriteJsonAsync(context, 200, "{\"id\":\"cc-3\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"call-weather-1\",\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"arguments\":\"{\\\"city\\\":\\\"Seattle\\\"}\"}}]},\"finish_reason\":\"tool_calls\",\"index\":0}]}").ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context, 200, "{\"id\":\"cc-4\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"finish_reason\":\"stop\",\"index\":0}]}").ConfigureAwait(false);
        }

        private async Task HandleOllamaAsync(HttpListenerContext context, bool stream, bool hasTools)
        {
            if (stream && hasTools)
            {
                await BeginStream(context, "application/x-ndjson").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"Checking \"},\"done\":false}\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"weather. \"},\"done\":false}\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"index\":0,\"name\":\"get_weather\",\"arguments\":{\"city\":\"Seattle\"}}}]},\"done\":false}\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"tool_calls\",\"prompt_eval_count\":11,\"eval_count\":7,\"total_duration\":1000,\"load_duration\":100,\"prompt_eval_duration\":200,\"eval_duration\":300}\n").ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (stream)
            {
                await BeginStream(context, "application/x-ndjson").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"hello \"},\"done\":false}\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"world\"},\"done\":false}\n").ConfigureAwait(false);
                await WriteChunkAsync(context, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true,\"done_reason\":\"stop\",\"prompt_eval_count\":3,\"eval_count\":2,\"total_duration\":1000,\"load_duration\":100,\"prompt_eval_duration\":200,\"eval_duration\":300}\n").ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (hasTools)
            {
                await WriteJsonAsync(context, 200, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"\",\"tool_calls\":[{\"function\":{\"name\":\"get_weather\",\"arguments\":{\"city\":\"Seattle\"}}}]},\"done\":true,\"done_reason\":\"tool_calls\"}").ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(context, 200, "{\"model\":\"test-model\",\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"done\":true,\"done_reason\":\"stop\"}").ConfigureAwait(false);
        }

        private static async Task BeginStream(HttpListenerContext context, string contentType)
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = contentType;
            context.Response.SendChunked = true;
            await Task.CompletedTask.ConfigureAwait(false);
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static async Task WriteChunkAsync(HttpListenerContext context, string chunk)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(chunk);
            await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            await context.Response.OutputStream.FlushAsync().ConfigureAwait(false);
        }

        private static int GetFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
