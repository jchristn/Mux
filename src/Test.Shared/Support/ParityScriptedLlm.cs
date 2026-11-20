namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A local OpenAI-compatible model scripted by the prompt, for the hub's mobile-parity routes and events:
    /// a prompt containing <c>ASK_USER</c> calls <c>ask_user</c>; <c>READ_FILE:&lt;path&gt;</c> calls <c>read_file</c>;
    /// any other turn answers with a long reply (long enough to cross the AI-title threshold). After a tool result it
    /// answers <c>TOOL_RESULT: &lt;result&gt;</c>. Requests without tools (title and compaction calls) get a title when the
    /// system prompt mentions a title, else a summary. Streaming and non-streaming requests are both served.
    /// </summary>
    internal sealed class ParityScriptedLlm : IDisposable
    {
        #region Public-Members

        /// <summary>The OpenAI-compatible base URL (ends in <c>/v1</c>).</summary>
        public string BaseUrl { get; }

        /// <summary>The title every title request receives.</summary>
        public const string Title = "Parity Scripted Title";

        /// <summary>The summary every compaction request receives.</summary>
        public const string Summary = "SUMMARY of the earlier conversation.";

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cancellation = new CancellationTokenSource();
        private readonly Task _Loop;

        #endregion

        #region Constructors-and-Factories

        private ParityScriptedLlm(HttpListener listener, string prefix)
        {
            _Listener = listener;
            BaseUrl = prefix.TrimEnd('/') + "/v1";
            _Loop = Task.Run(ListenLoopAsync);
        }

        /// <summary>
        /// Starts the scripted model on a free loopback port.
        /// </summary>
        /// <returns>The running model.</returns>
        public static ParityScriptedLlm Start()
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
                    return new ParityScriptedLlm(listener, prefix);
                }
                catch (Exception ex) when ((ex is HttpListenerException || ex is SocketException) && attempt < 10)
                {
                    try { listener.Close(); } catch (Exception) { }
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stops the model.
        /// </summary>
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

                bool stream = true;
                bool hasTools = false;
                string systemText = string.Empty;
                string lastRole = string.Empty;
                string lastContent = string.Empty;
                string lastUser = string.Empty;
                using (JsonDocument doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement root = doc.RootElement;
                    stream = root.TryGetProperty("stream", out JsonElement s) && s.ValueKind == JsonValueKind.True;
                    hasTools = root.TryGetProperty("tools", out JsonElement tools) && tools.ValueKind == JsonValueKind.Array && tools.GetArrayLength() > 0;
                    if (root.TryGetProperty("messages", out JsonElement messages) && messages.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement message in messages.EnumerateArray())
                        {
                            string role = message.TryGetProperty("role", out JsonElement r) ? r.GetString() ?? string.Empty : string.Empty;
                            string content = message.TryGetProperty("content", out JsonElement c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
                            if (role == "system") systemText += content;
                            if (role == "user") lastUser = content;
                            lastRole = role;
                            lastContent = content;
                        }
                    }
                }

                string? text = null;
                string? toolName = null;
                string? toolArguments = null;
                if (!hasTools)
                {
                    text = systemText.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0 ? Title : Summary;
                }
                else if (lastRole == "tool")
                {
                    string excerpt = lastContent.Length > 200 ? lastContent.Substring(0, 200) : lastContent;
                    text = "TOOL_RESULT: " + excerpt;
                }
                else if (lastUser.Contains("ASK_USER", StringComparison.Ordinal))
                {
                    toolName = "ask_user";
                    toolArguments = "{\"question\":\"Which database?\",\"options\":[{\"label\":\"Postgres\",\"description\":\"Server database\"},{\"label\":\"SQLite\",\"description\":\"Embedded file\"}]}";
                }
                else if (lastUser.Contains("READ_FILE:", StringComparison.Ordinal))
                {
                    string target = lastUser.Substring(lastUser.IndexOf("READ_FILE:", StringComparison.Ordinal) + 10).Trim().Split(' ')[0];
                    toolName = "read_file";
                    toolArguments = JsonSerializer.Serialize(new Dictionary<string, string> { ["file_path"] = target });
                }
                else
                {
                    text = "PARITY_REPLY " + new string('x', 320);
                }

                if (stream)
                {
                    StringBuilder sse = new StringBuilder();
                    if (toolName != null)
                    {
                        string delta = "{\"role\":\"assistant\",\"tool_calls\":[{\"index\":0,\"id\":\"call_parity_1\",\"type\":\"function\",\"function\":{\"name\":\"" + toolName + "\",\"arguments\":" + JsonSerializer.Serialize(toolArguments) + "}}]}";
                        sse.Append(Chunk(delta, null));
                        sse.Append(Chunk("{}", "tool_calls"));
                    }
                    else
                    {
                        sse.Append(Chunk(JsonSerializer.Serialize(new Dictionary<string, string> { ["content"] = text ?? string.Empty }), null));
                        sse.Append(Chunk("{}", "stop"));
                    }

                    sse.Append("data: [DONE]\n\n");
                    await WriteAsync(context, "text/event-stream", sse.ToString()).ConfigureAwait(false);
                }
                else
                {
                    string json = "{\"id\":\"parity\",\"object\":\"chat.completion\",\"model\":\"scripted\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":"
                        + JsonSerializer.Serialize(text ?? string.Empty) + "},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":5,\"total_tokens\":15}}";
                    await WriteAsync(context, "application/json", json).ConfigureAwait(false);
                }
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
